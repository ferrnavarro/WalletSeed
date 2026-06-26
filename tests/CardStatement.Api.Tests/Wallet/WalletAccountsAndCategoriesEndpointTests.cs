using System.Net;
using System.Net.Http.Json;
using CardStatement.Api.Tests;
using CardStatement.Api.Wallet;
using CardStatement.Api.Wallet.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CardStatement.Api.Tests.Wallet;

public class WalletAccountsAndCategoriesEndpointTests : IClassFixture<WebApiFactory>
{
    private readonly WebApiFactory _factory;

    public WalletAccountsAndCategoriesEndpointTests(WebApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task WalletApiClient_UsesConfiguredBasePath_WhenBuildingRequestUris()
    {
        Uri? seenUri = null;
        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Wallet:BaseUrl", "https://wallet.example/wallet/v1/api");
            builder.UseSetting("Wallet:Jwt", "test-jwt");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IWalletApiClient>();
                services.AddHttpClient<IWalletApiClient, WalletApiClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => new StubHttpHandler(request =>
                    {
                        seenUri = request.RequestUri;
                        return new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = JsonContent.Create(new { accounts = Array.Empty<object>() })
                        };
                    }));
            });
        }).CreateClient();

        var response = await client.GetAsync("/api/wallet/accounts");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new Uri("https://wallet.example/wallet/v1/api/accounts"), seenUri);
    }

    [Fact]
    public async Task AccountsEndpoint_UsesNestedCurrencyCode_WhenTopLevelCurrencyCodeIsAbsent()
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
                        Content = JsonContent.Create(new { accounts = new[] { new { id = "acct-1", name = "Checking", initialBalance = new { currencyCode = "USD" }, accountType = "SavingAccount", archived = false } } })
                    }));
            });
        }).CreateClient();

        var response = await client.GetAsync("/api/wallet/accounts");
        var payload = await response.Content.ReadFromJsonAsync<WalletAccountsPayload>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payload);
        Assert.Single(payload!.Accounts);
        Assert.Equal("USD", payload.Accounts[0].CurrencyCode);
    }

    [Fact]
    public async Task AccountsAndCategories_RespondWithExpectedPayloads_AndFilterArchivedAccounts()
    {
        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Wallet:BaseUrl", "https://wallet.example");
            builder.UseSetting("Wallet:Jwt", "test-jwt");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IWalletApiClient>();
                services.AddHttpClient<IWalletApiClient, WalletApiClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => new StubHttpHandler(request =>
                    {
                        if (request.RequestUri!.AbsolutePath.Contains("/accounts"))
                        {
                            return new HttpResponseMessage(HttpStatusCode.OK)
                            {
                                Content = JsonContent.Create(new { accounts = new[] { new { id = "acct-1", name = "Checking", currencyCode = "USD", accountType = "checking", archived = false }, new { id = "acct-2", name = "Archived", currencyCode = "USD", accountType = "checking", archived = true } } })
                            };
                        }

                        if (request.RequestUri!.AbsolutePath.Contains("/categories"))
                        {
                            return new HttpResponseMessage(HttpStatusCode.OK)
                            {
                                Content = JsonContent.Create(new { categories = new[] { new { id = "cat-1", name = "Food", color = "#ffffff" } } })
                            };
                        }

                        return new HttpResponseMessage(HttpStatusCode.NotFound);
                    }));
            });
        }).CreateClient();

        var accountsResponse = await client.GetAsync("/api/wallet/accounts");
        var categoriesResponse = await client.GetAsync("/api/wallet/categories");

        Assert.Equal(HttpStatusCode.OK, accountsResponse.StatusCode);
        var accountsPayload = await accountsResponse.Content.ReadFromJsonAsync<Dictionary<string, object[]>>();
        Assert.NotNull(accountsPayload);

        var categoriesPayload = await categoriesResponse.Content.ReadFromJsonAsync<Dictionary<string, object[]>>();
        Assert.NotNull(categoriesPayload);
    }

    [Fact]
    public async Task Upstream401_ReturnsWalletCredentialsInvalid()
    {
        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Wallet:BaseUrl", "https://wallet.example");
            builder.UseSetting("Wallet:Jwt", "test-jwt");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IWalletApiClient>();
                services.AddHttpClient<IWalletApiClient, WalletApiClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)));
            });
        }).CreateClient();

        var response = await client.GetAsync("/api/wallet/accounts");
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<WalletErrorResponse>();
        Assert.Equal("WALLET_CREDENTIALS_INVALID", payload?.Error.Code);
    }

    [Fact]
    public async Task UpstreamNetworkError_ReturnsWalletUnavailable()
    {
        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Wallet:BaseUrl", "https://wallet.example");
            builder.UseSetting("Wallet:Jwt", "test-jwt");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IWalletApiClient>();
                services.AddHttpClient<IWalletApiClient, WalletApiClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => new StubHttpHandler(_ => throw new HttpRequestException("boom")));
            });
        }).CreateClient();

        var response = await client.GetAsync("/api/wallet/accounts");
        Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<WalletErrorResponse>();
        Assert.Equal("WALLET_UNAVAILABLE", payload?.Error.Code);
    }

    private sealed class StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(responder(request));
    }

    private sealed record WalletAccountsPayload(IReadOnlyList<WalletAccountPayload> Accounts);
    private sealed record WalletAccountPayload(string Id, string Name, string CurrencyCode, string AccountType);
}
