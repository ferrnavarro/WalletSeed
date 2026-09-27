using CardStatement.Api.Wallet;
using CardStatement.Api.Wallet.Contracts;
using CardStatement.Core.Abstractions;
using CardStatement.Core.Banks;
using CardStatement.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CardStatement.Api.Tests.Wallet;

public class WalletImportEndToEndTests
{
    [Fact]
    public async Task CompareSubmitAndCompareAgain_PreservesImportedRows()
    {
        var walletClient = new InMemoryWalletClient();
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

        var initialCompare = await service.CompareAsync("/tmp/sample.pdf", "acct-1");
        Assert.Single(initialCompare.PdfRows);
        Assert.Empty(initialCompare.WalletRows);

        var submitResponse = await service.SubmitAsync(new SubmitRequest("acct-1", new[]
        {
            new SubmitRequestRow(0, new DateOnly(2026, 6, 10), -12.50m, "USD", "Coffee", null, "MAIN", "cat-1")
        }));

        Assert.Single(submitResponse.Outcomes);
        Assert.True(submitResponse.Outcomes[0].Ok);

        var followUpCompare = await service.CompareAsync("/tmp/sample.pdf", "acct-1");
        Assert.Single(followUpCompare.WalletRows);
        Assert.Equal("Coffee", followUpCompare.WalletRows[0].Note);
        Assert.Contains(0, followUpCompare.WalletRows[0].ClaimedByPdfIndices);
    }

    private sealed class InMemoryWalletClient : IWalletApiClient
    {
        private readonly List<WalletRecord> _records = [];

        public Task<IReadOnlyList<WalletAccount>> ListAccountsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<WalletAccount>>([
            new WalletAccount("acct-1", "Main", "USD", "checking", false)
        ]);

        public Task<IReadOnlyList<WalletCategory>> ListCategoriesAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<WalletCategory>>([
            new WalletCategory("cat-1", "Food", null)
        ]);

        public Task<IReadOnlyList<WalletRecord>> ListRecordsAsync(string accountId, DateOnly from, DateOnly to, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<WalletRecord>>(_records);

        public Task<IReadOnlyList<WalletLabel>> ListLabelsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<WalletLabel>>([
            new WalletLabel("lbl-main", "Main Label", null, false)
        ]);

        public Task<IReadOnlyList<WalletCreateOutcome>> CreateRecordsAsync(IReadOnlyList<WalletCreateRequest> rows, CancellationToken ct = default)
        {
            foreach (var row in rows)
            {
                _records.Add(new WalletRecord($"wr-{_records.Count + 1}", DateOnly.FromDateTime(row.RecordDate.UtcDateTime), row.SignedAmount, row.CurrencyCode ?? "USD", row.Note, row.CounterParty, row.CategoryId));
            }

            return Task.FromResult<IReadOnlyList<WalletCreateOutcome>>(rows.Select((_, index) => new WalletCreateOutcome(index, true, $"wr-{index + 1}", null)).ToList());
        }
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
