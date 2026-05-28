using CardStatement.Core.Apis;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace CardStatement.Tests.Apis;

public class LabelApiClientTests
{
    private const string SinglePage = """
    {
      "labels": [
        { "id": "936a90c7-01c4-4bf4-805a-59733a925547", "name": "BAC Titular", "color": "#212121", "archived": false },
        { "id": "16aa3eb4-e545-47d2-a45a-135b3475ac81", "name": "BAC adicional(David)", "color": "#212121", "archived": false },
        { "id": "00000000-0000-0000-0000-000000000099", "name": "OLD TAG", "color": "#000000", "archived": true }
      ],
      "limit": 30,
      "offset": 0
    }
    """;

    [Fact]
    public async Task Filters_archived_labels()
    {
        var handler = new FakeHttpHandler(_ => FakeHttpHandler.Json(SinglePage));
        var client = MakeClient(handler);

        var labels = await client.GetAllAsync();

        labels.Should().HaveCount(2);
        labels.Should().NotContain(l => l.Name == "OLD TAG");
        labels.Should().Contain(l => l.Name == "BAC Titular");
    }

    [Fact]
    public async Task Stops_when_returned_count_less_than_limit()
    {
        var handler = new FakeHttpHandler(_ => FakeHttpHandler.Json(SinglePage));
        var client = MakeClient(handler);

        _ = await client.GetAllAsync();

        handler.RequestedUrls.Should().HaveCount(1);
    }

    private static LabelApiClient MakeClient(FakeHttpHandler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/v1/api/") };
        var options = Options.Create(new ApiOptions
        {
            BaseUrl = "https://example.test/v1/api/",
            BearerToken = "TEST",
            PageSize = 30,
        });
        return new LabelApiClient(http, options);
    }
}
