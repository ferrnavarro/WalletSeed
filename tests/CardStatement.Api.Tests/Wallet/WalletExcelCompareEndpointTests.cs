using System.Net;
using System.Net.Http.Json;
using System.Text;
using CardStatement.Api.Wallet;
using CardStatement.Api.Wallet.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CardStatement.Api.Tests.Wallet;

public class WalletExcelCompareEndpointTests : IClassFixture<WebApiFactory>
{
    private readonly WebApiFactory _factory;

    public WalletExcelCompareEndpointTests(WebApiFactory factory)
    {
        _factory = factory;
    }

    private static readonly string SamplesDir = Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "promericaexcel");

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

    private static string? SampleFile()
    {
        var dir = Path.GetFullPath(SamplesDir);
        if (!Directory.Exists(dir)) return null;
        return Directory.GetFiles(dir, "*.xlsx").FirstOrDefault();
    }

    private static string? CuscatlanSampleFile()
    {
        var dir = Path.GetFullPath(Path.Combine(SamplesDir, "..", "cuscaexcel"));
        if (!Directory.Exists(dir)) return null;
        return Directory.GetFiles(dir, "*.xlsx").FirstOrDefault();
    }

    [Fact]
    public async Task CompareExcel_WithCuscatlanFile_AutoDetectsAndFlipsSigns()
    {
        var sample = CuscatlanSampleFile();
        if (sample is null) return;

        var client = CreateClientWithStubWallet();
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("acct-1"), "accountId");
        var bytes = await File.ReadAllBytesAsync(sample);
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        form.Add(content, "files", Path.GetFileName(sample));

        var response = await client.PostAsync("/api/wallet/import/compare-excel", form);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<FileImportCompareResponse>();
        Assert.NotNull(payload);
        Assert.NotEmpty(payload.PdfRows);
        Assert.Empty(payload.FileErrors);

        Assert.All(payload.PdfRows, row =>
        {
            Assert.Equal("USD", row.Currency);
            Assert.Equal("4502", row.CardLast4);
        });

        // Cuscatlán positive Dólares = expense → negative signedAmount
        var purchase = payload.PdfRows.FirstOrDefault(r => r.Description.Contains("AMAZON"));
        Assert.NotNull(purchase);
        Assert.True(purchase!.SignedAmount < 0);

        // Negative Dólares (PAGO RECIBIDO) = income → positive signedAmount
        var payment = payload.PdfRows.FirstOrDefault(r => r.Description.StartsWith("PAGO RECIBIDO"));
        Assert.NotNull(payment);
        Assert.True(payment!.SignedAmount > 0);
    }

    [Fact]
    public async Task CompareExcel_MixedBanks_MergesRows()
    {
        var promerica = SampleFile();
        var cuscatlan = CuscatlanSampleFile();
        if (promerica is null || cuscatlan is null) return;

        var client = CreateClientWithStubWallet();
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("acct-1"), "accountId");
        foreach (var path in new[] { promerica, cuscatlan })
        {
            var bytes = await File.ReadAllBytesAsync(path);
            var content = new ByteArrayContent(bytes);
            content.Headers.ContentType = new("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
            form.Add(content, "files", Path.GetFileName(path));
        }

        var response = await client.PostAsync("/api/wallet/import/compare-excel", form);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<FileImportCompareResponse>();
        Assert.NotNull(payload);
        Assert.Empty(payload.FileErrors);

        // 39 Promerica rows + however many the Cuscatlán sample has (9-14 depending on FS order)
        var cuscatlanRows = payload.PdfRows.Count - 39;
        Assert.InRange(cuscatlanRows, 9, 14);
        Assert.Equal(Enumerable.Range(0, payload.PdfRows.Count), payload.PdfRows.Select(r => r.Index));
        Assert.Contains(payload.PdfRows, r => r.CardLast4 == "3326");
        Assert.Contains(payload.PdfRows, r => r.CardLast4 == "4502");
    }

    [Fact]
    public async Task CompareExcel_UnrecognizedLayout_ReportsFileError()
    {
        // A syntactically valid xlsx that matches neither bank layout
        using var ms = new MemoryStream();
        using (var wb = new ClosedXML.Excel.XLWorkbook())
        {
            var ws = wb.AddWorksheet("Sheet1");
            ws.Cell(1, 1).Value = "Totally different layout";
            wb.SaveAs(ms);
        }

        var client = CreateClientWithStubWallet();
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("acct-1"), "accountId");
        form.Add(new ByteArrayContent(ms.ToArray()), "files", "unknown.xlsx");

        var response = await client.PostAsync("/api/wallet/import/compare-excel", form);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CompareExcel_WithValidFile_ReturnsRowsWithCorrectSigns()
    {
        var sample = SampleFile();
        if (sample is null) return;

        var client = CreateClientWithStubWallet();
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("acct-1"), "accountId");
        var bytes = await File.ReadAllBytesAsync(sample);
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        form.Add(content, "files", Path.GetFileName(sample));

        var response = await client.PostAsync("/api/wallet/import/compare-excel", form);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<FileImportCompareResponse>();
        Assert.NotNull(payload);
        Assert.Equal(39, payload.PdfRows.Count);
        Assert.Empty(payload.FileErrors);

        Assert.All(payload.PdfRows, row =>
        {
            Assert.Equal("USD", row.Currency);
            Assert.Equal(string.Empty, row.CardholderSectionRawName);
            Assert.Empty(row.PreviewLabelIds);
        });

        // Debitos → negative (row 9: CARGO DE INTERESES, $0.62 debit)
        var interest = payload.PdfRows.FirstOrDefault(r => r.Description == "CARGO DE INTERESES");
        Assert.NotNull(interest);
        Assert.True(interest!.SignedAmount < 0);
        Assert.Equal("0000", interest.CardLast4); // Tarjeta = "0"

        // Creditos → positive (row 47: "Pago", $2126.28 credit)
        var payment = payload.PdfRows.FirstOrDefault(r => r.Description == "Pago");
        Assert.NotNull(payment);
        Assert.Equal(2126.28m, payment!.SignedAmount);

        // Real description rows come through trimmed (row 11: SELECTOS LAS CASCADAS)
        var selectos = payload.PdfRows.FirstOrDefault(r => r.Description == "SELECTOS LAS CASCADAS");
        Assert.NotNull(selectos);
        Assert.True(selectos!.SignedAmount < 0);
        Assert.Equal("3326", selectos.CardLast4);

        // Sequential indices; all default-selected (stub returns no wallet records)
        Assert.Equal(Enumerable.Range(0, payload.PdfRows.Count), payload.PdfRows.Select(r => r.Index));
        Assert.All(payload.PdfRows, row => Assert.True(row.DefaultSelected));
    }

    [Fact]
    public async Task CompareExcel_WithOneBadFile_ReturnsRowsAndFileError()
    {
        var sample = SampleFile();
        if (sample is null) return;

        var client = CreateClientWithStubWallet();
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("acct-1"), "accountId");

        var bytes = await File.ReadAllBytesAsync(sample);
        var good = new ByteArrayContent(bytes);
        form.Add(good, "files", Path.GetFileName(sample));

        var bad = new ByteArrayContent(Encoding.UTF8.GetBytes("not an excel file"));
        form.Add(bad, "files", "broken.xlsx");

        var response = await client.PostAsync("/api/wallet/import/compare-excel", form);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<FileImportCompareResponse>();
        Assert.NotNull(payload);
        Assert.NotEmpty(payload.PdfRows);
        Assert.Single(payload.FileErrors);
        Assert.Equal("broken.xlsx", payload.FileErrors[0].FileName);
    }

    [Fact]
    public async Task CompareExcel_AllFilesBad_Returns400()
    {
        var client = CreateClientWithStubWallet();
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("acct-1"), "accountId");
        var bad = new ByteArrayContent(Encoding.UTF8.GetBytes("garbage"));
        form.Add(bad, "files", "bad.xlsx");

        var response = await client.PostAsync("/api/wallet/import/compare-excel", form);
        Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"Expected 400 but got {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }

    [Fact]
    public async Task CompareExcel_NoFiles_Returns400()
    {
        var client = CreateClientWithStubWallet();
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("acct-1"), "accountId");

        var response = await client.PostAsync("/api/wallet/import/compare-excel", form);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private sealed class StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(responder(request));
    }
}
