using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using CardStatement.Api.Tests.Fixtures;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace CardStatement.Api.Tests;

public sealed class DeterminismTests : IClassFixture<DeterminismTests.Factory>
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

    private readonly HttpClient _client;

    public DeterminismTests(Factory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task PostBacSamplePdf_TenTimesSerially_ReturnsIdenticalJsonResponses()
    {
        string? firstResponseJson = null;

        for (int i = 0; i < 10; i++)
        {
            // Act
            using var content = new MultipartFormDataContent();
            using var fileStream = SamplePdf.OpenRead();
            using var streamContent = new StreamContent(fileStream);
            streamContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/pdf");
            content.Add(streamContent, "file", Path.GetFileName(SamplePdf.Path));

            var response = await _client.PostAsync("/api/statements/extract", content);
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var json = await response.Content.ReadAsStringAsync();
            
            // Assert bank selection is bac
            json.Should().Contain("\"id\":\"bac\"");
            json.Should().Contain("\"displayName\":\"BAC Credomatic (El Salvador)\"");

            if (firstResponseJson == null)
            {
                firstResponseJson = json;
            }
            else
            {
                // Assert string-identical JSON body
                json.Should().Be(firstResponseJson, $"response {i} must be identical to response 0");
            }
        }
    }
}
