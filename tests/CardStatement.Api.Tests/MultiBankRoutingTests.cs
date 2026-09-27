using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using CardStatement.Api.Contracts;
using CardStatement.Api.Tests.Fixtures;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CardStatement.Api.Tests;

public sealed class MultiBankRoutingTests : IClassFixture<MultiBankRoutingTests.Factory>
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
    private readonly JsonSerializerOptions _jsonOptions;

    public MultiBankRoutingTests(Factory factory)
    {
        _client = factory.CreateClient();
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
        _jsonOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    }

    private async Task<HttpResponseMessage> PostPdfAsync(string filePath)
    {
        using var content = new MultipartFormDataContent();
        using var fileStream = File.OpenRead(filePath);
        using var streamContent = new StreamContent(fileStream);
        streamContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/pdf");
        content.Add(streamContent, "file", Path.GetFileName(filePath));

        return await _client.PostAsync("/api/statements/extract", content);
    }

    [Fact]
    public async Task PostBacSamplePdf_Returns200_WithBacBankId()
    {
        // Act
        var response = await PostPdfAsync(SamplePdf.Path);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ExtractedStatementResponse>(_jsonOptions);
        result.Should().NotBeNull();
        result!.Bank.Should().NotBeNull();
        result.Bank.Id.Should().Be("bac");
        result.Bank.DisplayName.Should().Be("BAC Credomatic (El Salvador)");
    }

    [Fact]
    public async Task PostStubSamplePdf_Returns200_WithStubBankId()
    {
        // Arrange
        var stubPdfPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Pdfs", "stub-marker.pdf");
        File.Exists(stubPdfPath).Should().BeTrue($"stub PDF must exist at {stubPdfPath}");

        // Act
        var response = await PostPdfAsync(stubPdfPath);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ExtractedStatementResponse>(_jsonOptions);
        result.Should().NotBeNull();
        result!.Bank.Should().NotBeNull();
        result.Bank.Id.Should().Be("stub");
        result.Bank.DisplayName.Should().Be("Stub Test Bank");
    }

    [Fact]
    public async Task PostNeitherSamplePdf_Returns422_WithUnrecognizedLayout()
    {
        // Arrange
        var neitherPdfPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Pdfs", "neither.pdf");
        File.Exists(neitherPdfPath).Should().BeTrue($"neither PDF must exist at {neitherPdfPath}");

        // Act
        var response = await PostPdfAsync(neitherPdfPath);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var error = await response.Content.ReadFromJsonAsync<ExtractionErrorResponse>(_jsonOptions);
        error.Should().NotBeNull();
        error!.Error.Code.Should().Be(ErrorCodes.UnrecognizedLayout);
    }
}
