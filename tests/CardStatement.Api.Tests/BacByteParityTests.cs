using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using CardStatement.Api.Tests.Fixtures;
using FluentAssertions;
using Xunit;

namespace CardStatement.Api.Tests;

public sealed class BacByteParityTests : IClassFixture<WebApiFactory>
{
    private readonly HttpClient _client;

    public BacByteParityTests(WebApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ExtractedJson_MatchesBaseline_ExceptBankProperty()
    {
        // Arrange
        using var content = new MultipartFormDataContent();
        using var fileStream = SamplePdf.OpenRead();
        using var streamContent = new StreamContent(fileStream);
        streamContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/pdf");
        content.Add(streamContent, "file", Path.GetFileName(SamplePdf.Path));

        // Act
        var response = await _client.PostAsync("/api/statements/extract", content);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var responseJsonStr = await response.Content.ReadAsStringAsync();
        var responseNode = JsonNode.Parse(responseJsonStr);
        responseNode.Should().NotBeNull();
        
        // Remove the bank property
        var removed = responseNode!.AsObject().Remove("bank");
        removed.Should().BeTrue("the response JSON must contain the additive 'bank' property at the root");

        // Load baseline
        var baseDir = AppContext.BaseDirectory;
        var baselinePath = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..", "specs", "002-multi-bank-support", "baselines", "extract-001-baseline.json"));
        File.Exists(baselinePath).Should().BeTrue($"baseline JSON must exist at {baselinePath}");
        
        var baselineJsonStr = await File.ReadAllTextAsync(baselinePath);
        var baselineNode = JsonNode.Parse(baselineJsonStr);
        baselineNode.Should().NotBeNull();

        // Serialize both to normalized string (no indent, same property order / serializing behavior) and compare
        var options = new JsonSerializerOptions { WriteIndented = false };
        var normalizedResponse = responseNode.ToJsonString(options);
        var normalizedBaseline = baselineNode!.ToJsonString(options);

        normalizedResponse.Should().Be(normalizedBaseline);
    }
}
