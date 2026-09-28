using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CardStatement.Api.Profiles;
using CardStatement.Api.Tests;
using CardStatement.Api.Wallet;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace CardStatement.Api.Tests.Profiles;

public class ProfileTests : IClassFixture<WebApiFactory>
{
    private readonly WebApiFactory _factory;

    public ProfileTests(WebApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public void ProfileService_ListAndGet_ReturnsExpectedProfiles()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Profiles:Fernando:Name"] = "Fernando Magaña",
            ["Profiles:Fernando:WalletApiToken"] = "token-fernando",
            ["Profiles:Fernando:LabelMapping:CLAUDIA"] = "id-claudia",
            ["Profiles:Fatima:Name"] = "Fatima Orantes",
            ["Profiles:Fatima:WalletApiToken"] = "token-fatima",
        }).Build();

        var service = new ProfileService(config);
        var list = service.ListProfiles();

        Assert.Equal(2, list.Count);
        Assert.Contains(list, p => p.Id == "Fernando" && p.Name == "Fernando Magaña");
        Assert.Contains(list, p => p.Id == "Fatima" && p.Name == "Fatima Orantes");

        var fernando = service.GetProfile("fernando");
        Assert.NotNull(fernando);
        Assert.Equal("token-fernando", fernando!.WalletApiToken);
        Assert.Equal("id-claudia", fernando.LabelMapping["CLAUDIA"]);

        var fatima = service.GetProfile("FATIMA");
        Assert.NotNull(fatima);
        Assert.Equal("token-fatima", fatima!.WalletApiToken);

        Assert.Null(service.GetProfile("unknown"));
    }

    [Fact]
    public void ProfileContext_ResolvesProfileToken_AndMergesLabelMappings()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Profiles:Fernando:Name"] = "Fernando",
            ["Profiles:Fernando:WalletApiToken"] = "token-fernando",
            ["Profiles:Fernando:LabelMapping:CUSTOM"] = "profile-label-id",
            ["Profiles:Fernando:LabelMapping:OVERRIDE"] = "profile-override-id",
        }).Build();

        var service = new ProfileService(config);
        var walletOptions = Options.Create(new WalletOptions
        {
            Jwt = "global-jwt",
            LabelMapping = new Dictionary<string, string>
            {
                ["GLOBAL"] = "global-label-id",
                ["OVERRIDE"] = "global-override-id"
            }
        });

        // 1. With active profile
        var context = ProfileContext.CreateForTest(service, walletOptions, "Fernando");
        Assert.Equal("Fernando", context.ActiveProfileId);
        Assert.Equal("token-fernando", context.GetActiveWalletApiToken());

        var mappings = context.GetActiveLabelMapping();
        Assert.Equal("global-label-id", mappings["GLOBAL"]);
        Assert.Equal("profile-label-id", mappings["CUSTOM"]);
        Assert.Equal("profile-override-id", mappings["OVERRIDE"]); // profile overrides global

        // 2. Without active profile (fallback)
        var fallbackContext = ProfileContext.CreateForTest(service, walletOptions, null);
        Assert.Null(fallbackContext.ActiveProfileId);
        Assert.Equal("global-jwt", fallbackContext.GetActiveWalletApiToken());
        Assert.Equal("global-override-id", fallbackContext.GetActiveLabelMapping()["OVERRIDE"]);
    }

    [Fact]
    public async Task GetProfilesEndpoint_ReturnsSanitizedProfiles_WithoutTokens()
    {
        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Profiles:Fernando:Name", "Fernando");
            builder.UseSetting("Profiles:Fernando:WalletApiToken", "secret-token-fer");
            builder.UseSetting("Profiles:Fatima:Name", "Fatima");
            builder.UseSetting("Profiles:Fatima:WalletApiToken", "secret-token-fat");
        }).CreateClient();

        var response = await client.GetAsync("/api/profiles");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("secret-token-fer", json);
        Assert.DoesNotContain("secret-token-fat", json);
        Assert.DoesNotContain("WalletApiToken", json, StringComparison.OrdinalIgnoreCase);

        var profiles = await response.Content.ReadFromJsonAsync<List<ProfileDto>>();
        Assert.NotNull(profiles);
        Assert.Contains(profiles!, p => p.Id == "Fernando" && p.Name == "Fernando");
        Assert.Contains(profiles!, p => p.Id == "Fatima" && p.Name == "Fatima");
    }

    [Fact]
    public async Task WalletEndpoints_UseProfileSpecificToken_WhenXProfileHeaderIsProvided()
    {
        var capturedAuths = new List<string?>();

        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Wallet:BaseUrl", "https://wallet.example");
            builder.UseSetting("Profiles:Fernando:Name", "Fernando");
            builder.UseSetting("Profiles:Fernando:WalletApiToken", "jwt-for-fernando");
            builder.UseSetting("Profiles:Fatima:Name", "Fatima");
            builder.UseSetting("Profiles:Fatima:WalletApiToken", "jwt-for-fatima");

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IWalletApiClient>();
                services.AddHttpClient<IWalletApiClient, WalletApiClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => new StubHttpHandler(request =>
                    {
                        capturedAuths.Add(request.Headers.Authorization?.ToString());
                        return new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = JsonContent.Create(new { accounts = Array.Empty<object>() })
                        };
                    }));
            });
        }).CreateClient();

        // 1. Request with X-Profile: Fernando
        using var reqFernando = new HttpRequestMessage(HttpMethod.Get, "/api/wallet/accounts");
        reqFernando.Headers.Add("X-Profile", "Fernando");
        var respFernando = await client.SendAsync(reqFernando);
        Assert.Equal(HttpStatusCode.OK, respFernando.StatusCode);

        // 2. Request with X-Profile: Fatima
        using var reqFatima = new HttpRequestMessage(HttpMethod.Get, "/api/wallet/accounts");
        reqFatima.Headers.Add("X-Profile", "Fatima");
        var respFatima = await client.SendAsync(reqFatima);
        Assert.Equal(HttpStatusCode.OK, respFatima.StatusCode);

        Assert.Equal(2, capturedAuths.Count);
        Assert.Equal("Bearer jwt-for-fernando", capturedAuths[0]);
        Assert.Equal("Bearer jwt-for-fatima", capturedAuths[1]);
    }

    private sealed class StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}
