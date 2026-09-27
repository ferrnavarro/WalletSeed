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

    [Fact]
    public async Task SubmitEndpoint_WithCardLast4_SendsCardMappedLabelIds()
    {
        string? capturedBody = null;
        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Wallet:BaseUrl", "https://wallet.example");
            builder.UseSetting("Wallet:Jwt", "test-jwt");
            builder.UseSetting("Wallet:LabelMapping:2533", "lbl-2533");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IWalletApiClient>();
                services.AddHttpClient<IWalletApiClient, WalletApiClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => new StubHttpHandler(async request =>
                    {
                        capturedBody = await request.Content!.ReadAsStringAsync();
                        return new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = JsonContent.Create(new { outcomes = new[] { new { success = true, id = "wr-1" } } })
                        };
                    }));
            });
        }).CreateClient();

        var payload = new SubmitRequest("acct-1", new[]
        {
            new SubmitRequestRow(0, new DateOnly(2026, 8, 18), -6.60m, "USD", "RESTAURANTE LA CABANA", null, string.Empty, "cat-1", "2533")
        });

        var response = await client.PostAsJsonAsync("/api/wallet/import/submit", payload);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(capturedBody);
        Assert.Contains("lbl-2533", capturedBody);
    }

    [Fact]
    public async Task SubmitEndpoint_WithoutCardMapping_FallsBackToNameMapping()
    {
        string? capturedBody = null;
        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Wallet:BaseUrl", "https://wallet.example");
            builder.UseSetting("Wallet:Jwt", "test-jwt");
            builder.UseSetting("Wallet:LabelMapping:MAIN", "lbl-main");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IWalletApiClient>();
                services.AddHttpClient<IWalletApiClient, WalletApiClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => new StubHttpHandler(async request =>
                    {
                        capturedBody = await request.Content!.ReadAsStringAsync();
                        return new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = JsonContent.Create(new { outcomes = new[] { new { success = true, id = "wr-1" } } })
                        };
                    }));
            });
        }).CreateClient();

        // PDF-style row: name-based section, no cardLast4
        var payload = new SubmitRequest("acct-1", new[]
        {
            new SubmitRequestRow(0, new DateOnly(2026, 6, 10), -12.50m, "USD", "Coffee", null, "MAIN", "cat-1")
        });

        var response = await client.PostAsJsonAsync("/api/wallet/import/submit", payload);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(capturedBody);
        Assert.Contains("lbl-main", capturedBody);
    }

    private sealed class StubHttpHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _responder;

        public StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
            : this(request => Task.FromResult(responder(request)))
        {
        }

        public StubHttpHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => _responder(request);
    }
}
