using CardStatement.Api.Wallet;
using CardStatement.Api.Wallet.Contracts;
using CardStatement.Core.Abstractions;
using CardStatement.Core.Banks;
using CardStatement.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CardStatement.Tests.Wallet;

public class WalletImportServiceCompareTests
{
    [Fact]
    public async Task CompareAsync_PopulatesPreviewLabelsFromConfiguredMappings()
    {
        var walletClient = new StubWalletClient(new[]
        {
            new WalletLabel("lbl-1", "Food", null, false)
        });

        var service = new WalletImportService(
            StubPdfExtractor.Instance,
            StubBankResolver.Instance,
            StubReconciler.Instance,
            walletClient,
            new LabelMappingResolver(Options.Create(new WalletOptions
            {
                LabelMapping = new Dictionary<string, string> { ["MAIN"] = "lbl-1" }
            })),
            Options.Create(new WalletOptions()),
            NullLogger<WalletImportService>.Instance);

        var response = await service.CompareAsync("/tmp/sample.pdf", "acct-1");

        var row = Assert.Single(response.PdfRows);
        Assert.Equal(new[] { "lbl-1" }, row.PreviewLabelIds);
        Assert.Equal(new[] { "Food" }, row.PreviewLabelNames);
    }

    private sealed class StubWalletClient : IWalletApiClient
    {
        private readonly IReadOnlyList<WalletLabel> _labels;

        public StubWalletClient(IReadOnlyList<WalletLabel> labels)
        {
            _labels = labels;
        }

        public Task<IReadOnlyList<WalletAccount>> ListAccountsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<WalletAccount>>([]);

        public Task<IReadOnlyList<WalletCategory>> ListCategoriesAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<WalletCategory>>([]);

        public Task<IReadOnlyList<WalletRecord>> ListRecordsAsync(string accountId, DateOnly from, DateOnly to, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<WalletRecord>>([]);

        public Task<IReadOnlyList<WalletLabel>> ListLabelsAsync(CancellationToken ct = default) => Task.FromResult(_labels);

        public Task<IReadOnlyList<WalletCreateOutcome>> CreateRecordsAsync(IReadOnlyList<WalletCreateRequest> rows, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<WalletCreateOutcome>>([]);
    }

    private sealed class StubPdfExtractor : IPdfExtractor
    {
        public static StubPdfExtractor Instance { get; } = new();
        public PdfDocumentWords Extract(string pdfPath) => new(1, [new PdfWord(1, "Coffee", 0, 0, 1, 1)]);
    }

    private sealed class StubBankResolver : IBankResolver
    {
        public static StubBankResolver Instance { get; } = new();
        public (BankInfo Bank, Statement Statement) Resolve(PdfDocumentWords words) => (new BankInfo("bac", "BAC"), new Statement
        {
            CardType = "Credit",
            MaskedAccount = "1234",
            Period = new StatementPeriod(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30)),
            PageCount = 1,
            Sections =
            [
                new CardholderSection
                {
                    CardLast4 = "1234",
                    RawName = "MAIN",
                    Transactions =
                    [
                        new Transaction
                        {
                            RawDescription = "Coffee",
                            TransactionDate = new DateOnly(2026, 6, 10),
                            PostingDate = new DateOnly(2026, 6, 10),
                            ReferenceNumber = "ref-1",
                            SequenceCode = "1",
                            RowType = RowType.Purchase,
                            Amount = 12.5m,
                            Direction = Direction.Expense,
                            CardLast4 = "1234"
                        }
                    ]
                }
            ]
        });
    }

    private sealed class StubReconciler : IReconciler
    {
        public static StubReconciler Instance { get; } = new();
        public Statement Reconcile(Statement statement) => statement;
    }
}
