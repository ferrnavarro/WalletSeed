using CardStatement.Api.Wallet;
using CardStatement.Api.Wallet.Contracts;
using CardStatement.Core.Abstractions;
using CardStatement.Core.Banks;
using CardStatement.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CardStatement.Tests.Wallet;

public class WalletImportServiceSubmitTests
{
    [Fact]
    public async Task SubmitAsync_MapsRowsToOutcomesAndPreservesOrder()
    {
        var walletClient = new StubWalletClient();
        var service = new WalletImportService(
            StubPdfExtractor.Instance,
            StubBankResolver.Instance,
            StubReconciler.Instance,
            walletClient,
            new LabelMappingResolver(Options.Create(new WalletOptions
            {
                LabelMapping = new Dictionary<string, string> { ["MAIN"] = "lbl-main" }
            })),
            Options.Create(new WalletOptions()),
            NullLogger<WalletImportService>.Instance);

        var request = new SubmitRequest("acct-1", new[]
        {
            new SubmitRequestRow(0, new DateOnly(2026, 6, 10), -12.50m, "USD", "Coffee", "Cafe", "MAIN", "cat-1"),
            new SubmitRequestRow(1, new DateOnly(2026, 6, 11), 200m, "USD", "Salary", null, "MAIN", "cat-2")
        });

        var response = await service.SubmitAsync(request);

        Assert.Equal(2, response.Outcomes.Count);
        Assert.Equal(0, response.Outcomes[0].Index);
        Assert.True(response.Outcomes[0].Ok);
        Assert.Equal("wr-1", response.Outcomes[0].WalletRecordId);
        Assert.Equal(1, response.Outcomes[1].Index);
        Assert.True(response.Outcomes[1].Ok);
        Assert.Equal("wr-2", response.Outcomes[1].WalletRecordId);
        Assert.Single(walletClient.Chunks);
        Assert.Equal(2, walletClient.Chunks[0].Count);
        Assert.Equal("lbl-main", walletClient.Chunks[0][0].LabelIds.Single());
    }

    [Fact]
    public async Task SubmitAsync_UsesBatchOffsetsAndStopsOnCredentialsError()
    {
        var walletClient = new ChunkingStubWalletClient(throwOnSecondChunk: true);
        var service = new WalletImportService(
            StubPdfExtractor.Instance,
            StubBankResolver.Instance,
            StubReconciler.Instance,
            walletClient,
            new LabelMappingResolver(Options.Create(new WalletOptions
            {
                LabelMapping = new Dictionary<string, string> { ["MAIN"] = "lbl-main" }
            })),
            Options.Create(new WalletOptions()),
            NullLogger<WalletImportService>.Instance);

        var request = new SubmitRequest("acct-1", Enumerable.Range(0, 60).Select(i =>
            new SubmitRequestRow(i, new DateOnly(2026, 6, 10 + (i % 20)), -12.50m, "USD", $"Coffee {i}", "Cafe", "MAIN", "cat-1")).ToArray());

        await Assert.ThrowsAsync<WalletApiException>(() => service.SubmitAsync(request));
        Assert.Equal(2, walletClient.Chunks.Count);
        Assert.Equal(50, walletClient.Chunks[0].Count);
        Assert.Equal(10, walletClient.Chunks[1].Count);
        Assert.Equal(0, walletClient.Chunks[0][0].AccountId.CompareTo("acct-1"));
    }

    [Fact]
    public async Task SubmitAsync_PreservesOriginalRowIndexesAcrossChunks()
    {
        var walletClient = new StubWalletClient();
        var service = new WalletImportService(
            StubPdfExtractor.Instance,
            StubBankResolver.Instance,
            StubReconciler.Instance,
            walletClient,
            new LabelMappingResolver(Options.Create(new WalletOptions
            {
                LabelMapping = new Dictionary<string, string> { ["MAIN"] = "lbl-main" }
            })),
            Options.Create(new WalletOptions()),
            NullLogger<WalletImportService>.Instance);

        var request = new SubmitRequest("acct-1", Enumerable.Range(0, 60).Select(i =>
            new SubmitRequestRow(i, new DateOnly(2026, 6, 10 + (i % 20)), -12.50m, "USD", $"Coffee {i}", "Cafe", "MAIN", "cat-1")).ToArray());

        var response = await service.SubmitAsync(request);

        Assert.Equal(2, walletClient.Chunks.Count);
        Assert.Equal(50, walletClient.Chunks[0].Count);
        Assert.Equal(10, walletClient.Chunks[1].Count);
        Assert.Equal(Enumerable.Range(0, 60), response.Outcomes.Select(o => o.Index));
        Assert.Equal(60, response.Outcomes.Count(o => o.Ok));
    }

    private sealed class StubWalletClient : IWalletApiClient
    {
        public List<List<WalletCreateRequest>> Chunks { get; } = new();

        public Task<IReadOnlyList<WalletAccount>> ListAccountsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<WalletAccount>>([]);

        public Task<IReadOnlyList<WalletCategory>> ListCategoriesAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<WalletCategory>>([]);

        public Task<IReadOnlyList<WalletRecord>> ListRecordsAsync(string accountId, DateOnly from, DateOnly to, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<WalletRecord>>([]);

        public Task<IReadOnlyList<WalletLabel>> ListLabelsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<WalletLabel>>([]);

        public Task<IReadOnlyList<WalletCreateOutcome>> CreateRecordsAsync(IReadOnlyList<WalletCreateRequest> rows, CancellationToken ct = default)
        {
            Chunks.Add(rows.ToList());
            var outcomes = rows.Select((row, index) => new WalletCreateOutcome(index, true, $"wr-{index + 1}", null)).ToList();
            return Task.FromResult<IReadOnlyList<WalletCreateOutcome>>(outcomes);
        }
    }

    private sealed class ChunkingStubWalletClient(bool throwOnSecondChunk) : IWalletApiClient
    {
        public List<List<WalletCreateRequest>> Chunks { get; } = new();

        public Task<IReadOnlyList<WalletAccount>> ListAccountsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<WalletAccount>>([]);

        public Task<IReadOnlyList<WalletCategory>> ListCategoriesAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<WalletCategory>>([]);

        public Task<IReadOnlyList<WalletRecord>> ListRecordsAsync(string accountId, DateOnly from, DateOnly to, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<WalletRecord>>([]);

        public Task<IReadOnlyList<WalletLabel>> ListLabelsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<WalletLabel>>([]);

        public Task<IReadOnlyList<WalletCreateOutcome>> CreateRecordsAsync(IReadOnlyList<WalletCreateRequest> rows, CancellationToken ct = default)
        {
            Chunks.Add(rows.ToList());
            if (throwOnSecondChunk && Chunks.Count == 2)
            {
                throw new WalletApiException(WalletApiErrorKind.CredentialsInvalid, "bad creds");
            }

            return Task.FromResult<IReadOnlyList<WalletCreateOutcome>>(rows.Select((row, index) => new WalletCreateOutcome(index, true, $"wr-{index + 1}", null)).ToList());
        }
    }

    private sealed class StubPdfExtractor : IPdfExtractor
    {
        public static StubPdfExtractor Instance { get; } = new();
        public PdfDocumentWords Extract(string pdfPath) => new(1, []);
    }

    private sealed class StubBankResolver : IBankResolver
    {
        public static StubBankResolver Instance { get; } = new();
        public (BankInfo Bank, Statement Statement) Resolve(PdfDocumentWords words) => (new BankInfo("bac", "BAC"), new Statement { CardType = "Credit", MaskedAccount = "1234", Period = new StatementPeriod(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30)), PageCount = 1, Sections = [] });
    }

    private sealed class StubReconciler : IReconciler
    {
        public static StubReconciler Instance { get; } = new();
        public Statement Reconcile(Statement statement) => statement;
    }
}
