using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using CardStatement.Api.Tests.Fixtures;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;
using Xunit.Abstractions;

namespace CardStatement.Api.Tests;

[Trait("Category", "Performance")]
public sealed class MultiBankLatencyTests : 
    IClassFixture<WebApiFactory>, 
    IClassFixture<MultiBankLatencyTests.Factory>
{
    public sealed class Factory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                services.AddStubBank();
            });
        }
    }

    private readonly HttpClient _bacOnlyClient;
    private readonly HttpClient _multiBankClient;
    private readonly ITestOutputHelper _output;

    public MultiBankLatencyTests(WebApiFactory bacOnlyFactory, Factory multiBankFactory, ITestOutputHelper output)
    {
        _bacOnlyClient = bacOnlyFactory.CreateClient();
        _multiBankClient = multiBankFactory.CreateClient();
        _output = output;
    }

    [Fact]
    public async Task PostBacSamplePdf_WithMultipleBanks_DoesNotDegradeLatencySignificantly()
    {
        const int WarmUpIterations = 15;
        const int TestIterations = 30;

        // Alternating warm up to ensure both pipelines are fully warm and JITted
        for (int i = 0; i < WarmUpIterations; i++)
        {
            await PostPdfAsync(_bacOnlyClient);
            await PostPdfAsync(_multiBankClient);
        }

        var bacOnlyTimes = new List<double>();
        var multiBankTimes = new List<double>();

        // Alternating measurements to cancel out transient temporal VM load variations
        for (int i = 0; i < TestIterations; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();

            // Measure BAC-only
            var sw1 = Stopwatch.StartNew();
            await PostPdfAsync(_bacOnlyClient);
            sw1.Stop();
            bacOnlyTimes.Add(sw1.Elapsed.TotalMilliseconds);

            // Measure BAC + Stub
            var sw2 = Stopwatch.StartNew();
            await PostPdfAsync(_multiBankClient);
            sw2.Stop();
            multiBankTimes.Add(sw2.Elapsed.TotalMilliseconds);
        }

        var medianBacOnly = GetMedian(bacOnlyTimes);
        var medianMultiBank = GetMedian(multiBankTimes);

        _output.WriteLine($"Median Latency (BAC only): {medianBacOnly:F2} ms");
        _output.WriteLine($"Median Latency (BAC + Stub): {medianMultiBank:F2} ms");

        // SC-005: Assert median(bac+stub) <= median(bac_only) * 1.10
        medianMultiBank.Should().BeLessThanOrEqualTo(medianBacOnly * 1.10, 
            "overhead of auto-detection must be <= 10% of parsing time");
    }

    private async Task PostPdfAsync(HttpClient client)
    {
        using var content = new MultipartFormDataContent();
        using var fileStream = SamplePdf.OpenRead();
        using var streamContent = new StreamContent(fileStream);
        streamContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/pdf");
        content.Add(streamContent, "file", Path.GetFileName(SamplePdf.Path));

        var response = await client.PostAsync("/api/statements/extract", content);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static double GetMedian(List<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        int count = sorted.Count;
        if (count == 0) return 0;
        if (count % 2 == 0)
        {
            return (sorted[count / 2 - 1] + sorted[count / 2]) / 2.0;
        }
        return sorted[count / 2];
    }
}
