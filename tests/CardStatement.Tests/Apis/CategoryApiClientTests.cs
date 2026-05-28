using CardStatement.Core.Apis;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace CardStatement.Tests.Apis;

public class CategoryApiClientTests
{
    private const string Page1 = """
    {
      "categories": [
        { "id": "041e43d7-6a9c-4acc-b877-29ceb0811fe4", "name": "Groceries", "color": "#FF3D00", "envelopeId": 1000 },
        { "id": "3df5bc6d-4c6d-40ae-8f0f-23ed3e35f810", "name": "", "envelopeId": 2004 },
        { "id": "10c12b30-bf94-4b58-bac8-3e2e4528feb6", "name": "Debt", "color": "#26c6da", "envelopeId": 20000 }
      ],
      "limit": 3,
      "offset": 0,
      "nextOffset": 3
    }
    """;

    private const string Page2 = """
    {
      "categories": [
        { "id": "40b565bb-d9cc-430a-a4ef-0c8649b636ab", "name": "Automatic bank statements reading", "envelopeId": 20005 }
      ],
      "limit": 3,
      "offset": 3,
      "nextOffset": null
    }
    """;

    [Fact]
    public async Task Follows_pagination_via_next_offset()
    {
        var handler = new FakeHttpHandler(req =>
            req.RequestUri!.Query.Contains("offset=3", StringComparison.Ordinal)
                ? FakeHttpHandler.Json(Page2)
                : FakeHttpHandler.Json(Page1));

        var client = MakeClient(handler, pageSize: 3);

        var categories = await client.GetAllAsync();

        categories.Should().HaveCount(3);
        categories.Should().ContainSingle(c => c.Name == "Groceries");
        categories.Should().ContainSingle(c => c.Name == "Debt");
        categories.Should().ContainSingle(c => c.Name == "Automatic bank statements reading");
        handler.RequestedUrls.Should().HaveCount(2);
    }

    [Fact]
    public async Task Skips_empty_name_entries()
    {
        var handler = new FakeHttpHandler(_ => FakeHttpHandler.Json("""
        {
          "categories": [
            { "id": "041e43d7-6a9c-4acc-b877-29ceb0811fe4", "name": "Groceries", "envelopeId": 1000 },
            { "id": "3df5bc6d-4c6d-40ae-8f0f-23ed3e35f810", "name": "", "envelopeId": 2004 }
          ],
          "limit": 30,
          "offset": 0,
          "nextOffset": null
        }
        """));

        var client = MakeClient(handler);

        var categories = await client.GetAllAsync();

        categories.Should().HaveCount(1);
        categories.Single().Name.Should().Be("Groceries");
    }

    private static CategoryApiClient MakeClient(FakeHttpHandler handler, int pageSize = 30)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/v1/api/") };
        var options = Options.Create(new ApiOptions
        {
            BaseUrl = "https://example.test/v1/api/",
            BearerToken = "TEST",
            PageSize = pageSize,
        });
        return new CategoryApiClient(http, options);
    }
}
