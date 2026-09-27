using System.Net;
using System.Net.Http.Json;
using CardStatement.Api.Tests;
using CardStatement.Api.Wallet;
using CardStatement.Api.Wallet.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CardStatement.Api.Tests.Wallet;

public class WalletSubmitEndpointTests : IClassFixture<WebApiFactory>
{
    private readonly WebApiFactory _factory;

    public WalletSubmitEndpointTests(WebApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task SubmitEndpoint_ReturnsOutcomes_ForSuccessfulBatch()
    {
        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Wallet:BaseUrl", "https://wallet.example");
            builder.UseSetting("Wallet:Jwt", "test-jwt");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IWalletApiClient>();
                services.AddHttpClient<IWalletApiClient, WalletApiClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = JsonContent.Create(new { outcomes = new[] { new { success = true, id = "wr-1" } } })
                    }));
            });
        }).CreateClient();

        var payload = new SubmitRequest("acct-1", new[]
        {
            new SubmitRequestRow(0, new DateOnly(2026, 6, 10), -12.50m, "USD", "Coffee", null, "MAIN", "cat-1")
        });

        var response = await client.PostAsJsonAsync("/api/wallet/import/submit", payload);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<SubmitResponse>();
        Assert.NotNull(body);
        Assert.Single(body.Outcomes);
        Assert.True(body.Outcomes[0].Ok);
    }

    [Fact]
    public async Task SubmitEndpoint_ReturnsBadRequest_WhenCategoryMissingOrAccountEmpty()
    {
        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Wallet:BaseUrl", "https://wallet.example");
            builder.UseSetting("Wallet:Jwt", "test-jwt");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IWalletApiClient>();
                services.AddHttpClient<IWalletApiClient, WalletApiClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = JsonContent.Create(new { outcomes = Array.Empty<object>() })
                    }));
            });
        }).CreateClient();

        var missingCategory = new SubmitRequest("acct-1", new[]
        {
            new SubmitRequestRow(0, new DateOnly(2026, 6, 10), -12.50m, "USD", "Coffee", null, "MAIN", string.Empty)
        });
        var missingAccount = new SubmitRequest(string.Empty, new[]
        {
            new SubmitRequestRow(0, new DateOnly(2026, 6, 10), -12.50m, "USD", "Coffee", null, "MAIN", "cat-1")
        });

        var missingCategoryResponse = await client.PostAsJsonAsync("/api/wallet/import/submit", missingCategory);
        var missingAccountResponse = await client.PostAsJsonAsync("/api/wallet/import/submit", missingAccount);

        Assert.Equal(HttpStatusCode.BadRequest, missingCategoryResponse.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, missingAccountResponse.StatusCode);
    }

    private sealed class StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(responder(request));
    }
}
