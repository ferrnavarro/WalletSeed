using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CardStatement.Api.Tests;
using CardStatement.Api.Tests.Fixtures;
using CardStatement.Api.Wallet;
using CardStatement.Api.Wallet.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CardStatement.Api.Tests.Wallet;

public class WalletCompareEndpointTests : IClassFixture<WebApiFactory>
{
    private readonly WebApiFactory _factory;

    public WalletCompareEndpointTests(WebApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CompareEndpoint_ReturnsRowsAndPreviewLabels_AndUsesDeterministicOrdering()
    {
        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Wallet:BaseUrl", "https://wallet.example");
            builder.UseSetting("Wallet:Jwt", "test-jwt");
            builder.UseSetting("Wallet:LabelMapping:CLAUDIA NAVARRO G", "lbl-main");
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
                                Content = JsonContent.Create(new { accounts = new[] { new { id = "acct-1", name = "Main", currencyCode = "USD", accountType = "checking", archived = false } } })
                            };
                        }

                        if (request.RequestUri!.AbsolutePath.Contains("/categories"))
                        {
                            return new HttpResponseMessage(HttpStatusCode.OK)
                            {
                                Content = JsonContent.Create(new { categories = Array.Empty<object>() })
                            };
                        }

                        if (request.RequestUri!.AbsolutePath.Contains("/records"))
                        {
                            return new HttpResponseMessage(HttpStatusCode.OK)
                            {
                                Content = JsonContent.Create(new { records = Array.Empty<object>() })
                            };
                        }

                        if (request.RequestUri!.AbsolutePath.Contains("/labels"))
                        {
                            return new HttpResponseMessage(HttpStatusCode.OK)
                            {
                                Content = JsonContent.Create(new { labels = new[] { new { id = "lbl-main", name = "Main Label", color = "#ffffff", archived = false } } })
                            };
                        }

                        return new HttpResponseMessage(HttpStatusCode.NotFound);
                    }));
            });
        }).CreateClient();

        using var formData = new MultipartFormDataContent();
        using var fileStream = SamplePdf.OpenRead();
        using var streamContent = new StreamContent(fileStream);
        streamContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/pdf");
        formData.Add(streamContent, "file", Path.GetFileName(SamplePdf.Path));
        formData.Add(new StringContent("acct-1"), "accountId");

        var response = await client.PostAsync("/api/wallet/import/compare", formData);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<CompareResponse>();
        Assert.NotNull(payload);
        Assert.NotEmpty(payload.PdfRows);
        Assert.Equal(Enumerable.Range(0, payload.PdfRows.Count), payload.PdfRows.Select(row => row.Index));
        Assert.Equal(new[] { "lbl-main" }, payload.PdfRows[0].PreviewLabelIds);
        Assert.Equal(new[] { "Main Label" }, payload.PdfRows[0].PreviewLabelNames);
    }

    private sealed class StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(responder(request));
    }
}
