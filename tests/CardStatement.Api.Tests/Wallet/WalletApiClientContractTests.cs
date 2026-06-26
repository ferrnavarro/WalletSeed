using System.Net;
using System.Text;
using System.Text.Json;
using CardStatement.Api.Wallet;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CardStatement.Api.Tests.Wallet;

public sealed class WalletApiClientContractTests
{
    [Fact]
    public async Task CreateRecordsAsync_sendsArrayPayload_and_parsesResultsShape()
    {
        var handler = new RecordingHandler(async request =>
        {
            var payload = await request.Content!.ReadAsStringAsync();
            var doc = JsonDocument.Parse(payload);
            var array = doc.RootElement;
            Assert.Equal(JsonValueKind.Array, array.ValueKind);
            Assert.Equal(1, array.GetArrayLength());

            var item = array[0];
            Assert.Equal("acct-1", item.GetProperty("accountId").GetString());
            Assert.Equal(-10.5m, item.GetProperty("amount").GetProperty("value").GetDecimal());
            Assert.Equal("USD", item.GetProperty("amount").GetProperty("currencyCode").GetString());
            Assert.Equal("credit_card", item.GetProperty("paymentType").GetString());
            Assert.Equal("cat-1", item.GetProperty("categoryId").GetString());
            Assert.Equal("note", item.GetProperty("note").GetString());
            Assert.Equal("counterparty", item.GetProperty("counterParty").GetString());
            Assert.Equal("2025-01-02T03:04:05Z", item.GetProperty("recordDate").GetString());

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"results\":[{\"inputIndex\":0,\"success\":true,\"id\":\"wr-1\",\"error\":null}]}", Encoding.UTF8, "application/json")
            };
        });

        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://wallet.test/v1/api/")
        };

        var sut = new WalletApiClient(
            client,
            Options.Create(new WalletOptions { BaseUrl = "https://wallet.test/v1/api", Jwt = "jwt" }),
            NullLogger<WalletApiClient>.Instance);

        var outcome = await sut.CreateRecordsAsync([
            new WalletCreateRequest(
                "acct-1",
                new DateTimeOffset(2025, 1, 2, 3, 4, 5, TimeSpan.Zero),
                -10.5m,
                "USD",
                "credit_card",
                "cat-1",
                ["lab-1"],
                "note",
                "counterparty")
        ]);

        Assert.Single(outcome);
        Assert.True(outcome[0].Success);
        Assert.Equal("wr-1", outcome[0].Id);
        Assert.Null(outcome[0].Error);
    }

    [Fact]
    public async Task ListAccountsAsync_followsWalletPagination()
    {
        var requestCount = 0;
        var handler = new RecordingHandler(async request =>
        {
            requestCount++;
            return requestCount switch
            {
                1 => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"accounts\":[{\"id\":\"acct-1\",\"name\":\"First\",\"currencyCode\":\"USD\",\"accountType\":\"checking\",\"archived\":false}],\"nextOffset\":1}", Encoding.UTF8, "application/json")
                },
                _ => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"accounts\":[{\"id\":\"acct-2\",\"name\":\"Second\",\"currencyCode\":\"USD\",\"accountType\":\"checking\",\"archived\":false}]}", Encoding.UTF8, "application/json")
                }
            };
        });

        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://wallet.test/v1/api/")
        };

        var sut = new WalletApiClient(
            client,
            Options.Create(new WalletOptions { BaseUrl = "https://wallet.test/v1/api", Jwt = "jwt" }),
            NullLogger<WalletApiClient>.Instance);

        var accounts = await sut.ListAccountsAsync();

        Assert.Equal(2, accounts.Count);
        Assert.Equal(["acct-1", "acct-2"], accounts.Select(a => a.Id));
        Assert.Equal(2, requestCount);
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return await handler(request);
        }
    }
}
