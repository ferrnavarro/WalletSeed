using CardStatement.Core.Categorization;
using CardStatement.Core.Labels;
using CardStatement.Core.Models;
using CardStatement.Core.Banks.Bac;
using CardStatement.Core.Pdf;
using CardStatement.Core.Reconciliation;
using CardStatement.Core.Result;
using FluentAssertions;

namespace CardStatement.Tests.EndToEnd;

public class SamplePipelineTests
{
    private const string SamplePath = "../../../../../samples/final5140_45178439_316493_0.pdf";

    private static readonly Guid GroceriesId = new("041e43d7-6a9c-4acc-b877-29ceb0811fe4");
    private static readonly Guid DebtId = new("10c12b30-bf94-4b58-bac8-3e2e4528feb6");
    private static readonly Guid LoanId = new("3aea0a15-d1b5-46fd-9965-98868dd410ca");
    private static readonly Guid RefundsId = new("0ba25396-1fd3-477d-9b73-100a4229942b");
    private static readonly Guid FallbackId = new("40b565bb-d9cc-430a-a4ef-0c8649b636ab");

    private static readonly Category[] Taxonomy =
    [
        new(GroceriesId, "Groceries"),
        new(DebtId, "Debt"),
        new(LoanId, "Loan, interests"),
        new(RefundsId, "Refunds (tax, purchase)"),
        new(FallbackId, "Automatic bank statements reading"),
    ];

    private static readonly Guid BacTitular = new("936a90c7-01c4-4bf4-805a-59733a925547");
    private static readonly Guid BacDavid = new("16aa3eb4-e545-47d2-a45a-135b3475ac81");
    private static readonly Guid BacFatima = new("7c4fe378-882a-49b2-b7de-3fb076694a01");
    private static readonly Guid BacMama = new("c049554c-b118-4e47-9aa5-9f863507cfeb");

    private static readonly Label[] Labels =
    [
        new(BacTitular, "BAC Titular"),
        new(BacDavid, "BAC adicional(David)"),
        new(BacFatima, "BAC adicional (Fátima)"),
        new(BacMama, "BAC adicional (Mamá)"),
    ];

    [Fact]
    public async Task Sample_pdf_parses_reconciles_and_totals_match_printed()
    {
        File.Exists(SamplePath).Should().BeTrue($"sample PDF must exist at {Path.GetFullPath(SamplePath)}");

        var extractor = new PdfPigExtractor();
        var parser = new BacStatementParser();
        var reconciler = new Reconciler();
        var statement = reconciler.Reconcile(parser.Parse(extractor.Extract(SamplePath)));

        statement.ReconciliationStatus.Should().Be(ReconciliationStatus.Ok);
        statement.PrintedTotalCharges.Should().Be(1462.19m);
        statement.PrintedTotalCredits.Should().Be(877.01m);
        statement.Sections.Should().HaveCount(5);
        statement.Sections.Select(s => s.CardLast4).Should().BeEquivalentTo(["2533", "2640", "2706", "4941", "5468"]);

        var labelMap = new Dictionary<string, Guid>
        {
            ["2533"] = BacMama,
            ["2640"] = BacFatima,
            ["2706"] = BacTitular,
            ["4941"] = BacDavid,
            ["5468"] = BacTitular,
        };
        var labelResolver = new LabelResolver(labelMap, Labels);
        var catOptions = new CategorizationOptions
        {
            FallbackCategoryId = FallbackId,
            FlagFallbackAsNeedsReview = true,
        };
        var fixedResolver = new FixedCategoryResolver(Taxonomy, catOptions.FixedCategoryNames, catOptions.FallbackCategoryId);
        var categorizer = new LlmCategorizer(new StubLlmClient(), fixedResolver, Taxonomy, catOptions);

        var result = await new ResultBuilder(labelResolver, categorizer).BuildAsync(statement);

        result.TotalExpense.Should().Be(1462.19m);
        result.TotalIncome.Should().Be(877.01m);
        result.ReconciliationStatus.Should().Be(ReconciliationStatus.Ok);
        result.UnmappedCards.Should().BeEmpty();

        result.Records.Should().AllSatisfy(r =>
        {
            r.LabelId.Should().NotBeNull("every card in the sample is mapped");
        });

        var payment = result.Records.Single(r => r.Description.Contains("SU PAGO RECIBIDO", StringComparison.Ordinal));
        payment.Direction.Should().Be(Direction.Income);
        payment.Amount.Should().Be(802.01m);
        payment.CategoryId.Should().Be(DebtId);

        var reversal = result.Records.Single(r => r.Description.Contains("REVERSION PLAN PRF", StringComparison.Ordinal));
        reversal.Direction.Should().Be(Direction.Income);
        reversal.Amount.Should().Be(75.00m);
        reversal.CategoryId.Should().Be(RefundsId);

        var financing = result.Records.Where(r => r.Description.StartsWith("PLAN PRF", StringComparison.Ordinal)).ToList();
        financing.Should().NotBeEmpty();
        financing.Should().AllSatisfy(r =>
        {
            r.Direction.Should().Be(Direction.Expense);
            r.CategoryId.Should().Be(LoanId);
        });

        result.Records.Where(r => r.LabelUnmapped).Should().BeEmpty();
        result.Records.Where(r => r.NeedsReview).Should().NotBeEmpty(
            "purchases routed through StubLlmClient fall back to the fallback bucket and are flagged");
    }
}
