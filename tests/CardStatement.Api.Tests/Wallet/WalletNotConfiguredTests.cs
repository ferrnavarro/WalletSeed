using System.Net;
using CardStatement.Api.Tests;

namespace CardStatement.Api.Tests.Wallet;

public class WalletNotConfiguredTests : IClassFixture<WebApiFactory>
{
    private readonly WebApiFactory _factory;

    public WalletNotConfiguredTests(WebApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task WalletEndpoints_Return503_WhenJwtMissing()
    {
        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Wallet:Jwt", string.Empty);
        }).CreateClient();

        var accountsResponse = await client.GetAsync("/api/wallet/accounts");
        var categoriesResponse = await client.GetAsync("/api/wallet/categories");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, accountsResponse.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, categoriesResponse.StatusCode);
    }
}
