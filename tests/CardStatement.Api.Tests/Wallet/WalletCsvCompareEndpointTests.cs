using System.Net;
using System.Net.Http.Json;
using System.Text;
using CardStatement.Api.Wallet;
using CardStatement.Api.Wallet.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CardStatement.Api.Tests.Wallet;

public class WalletCsvCompareEndpointTests : IClassFixture<WebApiFactory>
{
    private readonly WebApiFactory _factory;

    public WalletCsvCompareEndpointTests(WebApiFactory factory)
    {
        _factory = factory;
    }

    private static readonly string SamplesDir = Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "baccsv");

    private HttpClient CreateClientWithStubWallet()
    {
        return _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Wallet:BaseUrl", "https://wallet.example");
            builder.UseSetting("Wallet:Jwt", "test-jwt");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IWalletApiClient>();
                services.AddHttpClient<IWalletApiClient, WalletApiClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => new StubHttpHandler(request =>
                    {
                        var path = request.RequestUri!.AbsolutePath;
                        if (path.Contains("/accounts"))
                        {
                            return new HttpResponseMessage(HttpStatusCode.OK)
                            {
                                Content = JsonContent.Create(new { accounts = new[] { new { id = "acct-1", name = "Main", currencyCode = "USD", accountType = "creditcard", archived = false } } })
                            };
                        }
                        if (path.Contains("/categories"))
                        {
                            return new HttpResponseMessage(HttpStatusCode.OK)
                            {
                                Content = JsonContent.Create(new { categories = new[] { new { id = "cat-1", name = "Food", color = "#ff0000" } } })
                            };
                        }
                        if (path.Contains("/records"))
                        {
                            return new HttpResponseMessage(HttpStatusCode.OK)
                            {
                                Content = JsonContent.Create(new { records = Array.Empty<object>() })
                            };
                        }
                        return new HttpResponseMessage(HttpStatusCode.NotFound);
                    }));
            });
        }).CreateClient();
    }

    [Fact]
    public async Task CompareCsv_WithValidFiles_ReturnsMergedRows()
    {
        var sample1 = Path.GetFullPath(Path.Combine(SamplesDir, "Estado de cuenta.csv"));
        var sample2 = Path.GetFullPath(Path.Combine(SamplesDir, "Estado de cuenta(1).csv"));
        if (!File.Exists(sample1) || !File.Exists(sample2)) return;

        var client = CreateClientWithStubWallet();
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("acct-1"), "accountId");
        foreach (var path in new[] { sample1, sample2 })
        {
            var bytes = await File.ReadAllBytesAsync(path);
            var content = new ByteArrayContent(bytes);
            content.Headers.ContentType = new("text/csv");
            form.Add(content, "files", Path.GetFileName(path));
        }

        var response = await client.PostAsync("/api/wallet/import/compare-csv", form);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<CsvCompareResponse>();
        Assert.NotNull(payload);
        Assert.NotEmpty(payload.PdfRows);
        Assert.Empty(payload.FileErrors);

        // All rows: currency USD, sign flipped (CSV positive → negative), card last4 2127, empty section name
        Assert.All(payload.PdfRows, row =>
        {
            Assert.Equal("USD", row.Currency);
            Assert.Equal("2127", row.CardLast4);
            Assert.Equal(string.Empty, row.CardholderSectionRawName);
            Assert.Empty(row.PreviewLabelIds);
        });

        // Sign flip: a CSV expense (positive dollars) appears as negative signedAmount
        var laPampa = payload.PdfRows.FirstOrDefault(r => r.Description.Contains("LA PAMPA"));
        Assert.NotNull(laPampa);
        Assert.True(laPampa!.SignedAmount < 0);

        // Indices are sequential across merged files
        Assert.Equal(Enumerable.Range(0, payload.PdfRows.Count), payload.PdfRows.Select(r => r.Index));

        // No matches (stub returns no records) → all default-selected
        Assert.All(payload.PdfRows, row => Assert.True(row.DefaultSelected));
    }

    [Fact]
    public async Task CompareCsv_WithOneBadFile_ReturnsRowsAndFileError()
    {
        var sample1 = Path.GetFullPath(Path.Combine(SamplesDir, "Estado de cuenta.csv"));
        if (!File.Exists(sample1)) return;

        var client = CreateClientWithStubWallet();
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("acct-1"), "accountId");

        var bytes = await File.ReadAllBytesAsync(sample1);
        var good = new ByteArrayContent(bytes);
        good.Headers.ContentType = new("text/csv");
        form.Add(good, "files", Path.GetFileName(sample1));

        var bad = new ByteArrayContent(Encoding.UTF8.GetBytes("this is not,a valid\nbac,csv file"));
        bad.Headers.ContentType = new("text/csv");
        form.Add(bad, "files", "broken.csv");

        var response = await client.PostAsync("/api/wallet/import/compare-csv", form);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<CsvCompareResponse>();
        Assert.NotNull(payload);
        Assert.NotEmpty(payload.PdfRows);
        Assert.Single(payload.FileErrors);
        Assert.Equal("broken.csv", payload.FileErrors[0].FileName);
    }

    [Fact]
    public async Task CompareCsv_AllFilesBad_Returns400()
    {
        var client = CreateClientWithStubWallet();
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("acct-1"), "accountId");
        var bad = new ByteArrayContent(Encoding.UTF8.GetBytes("garbage"));
        bad.Headers.ContentType = new("text/csv");
        form.Add(bad, "files", "bad.csv");

        var response = await client.PostAsync("/api/wallet/import/compare-csv", form);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CompareCsv_NoFiles_Returns400()
    {
        var client = CreateClientWithStubWallet();
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("acct-1"), "accountId");

        var response = await client.PostAsync("/api/wallet/import/compare-csv", form);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CompareCsv_NoAccountId_Returns400()
    {
        var sample1 = Path.GetFullPath(Path.Combine(SamplesDir, "Estado de cuenta.csv"));
        if (!File.Exists(sample1)) return;

        var client = CreateClientWithStubWallet();
        using var form = new MultipartFormDataContent();
        var bytes = await File.ReadAllBytesAsync(sample1);
        var content = new ByteArrayContent(bytes);
        form.Add(content, "files", "file.csv");

        var response = await client.PostAsync("/api/wallet/import/compare-csv", form);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private sealed class StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(responder(request));
    }
}
