using CardStatement.Core.Models;
using CardStatement.Core.Reconciliation;
using FluentAssertions;

namespace CardStatement.Tests.Reconciliation;

public class ReconcilerTests
{
    [Fact]
    public void Sums_matching_printed_subtotal_marks_section_ok()
    {
        var statement = MakeStatement(
            sections:
            [
                MakeSection("2533", "X",
                    transactions: [Expense(10.00m), Expense(5.50m)],
                    printedCharges: 15.50m, printedCredits: null),
            ],
            printedTotalCharges: 15.50m, printedTotalCredits: null);

        var result = new Reconciler().Reconcile(statement);

        result.Sections.Single().ReconciliationStatus.Should().Be(ReconciliationStatus.Ok);
        result.ReconciliationStatus.Should().Be(ReconciliationStatus.Ok);
    }

    [Fact]
    public void Sum_mismatch_marks_section_and_statement_as_mismatch()
    {
        var statement = MakeStatement(
            sections:
            [
                MakeSection("2533", "X",
                    transactions: [Expense(10.00m), Expense(5.50m)],
                    printedCharges: 99.99m, printedCredits: null),
            ],
            printedTotalCharges: 99.99m, printedTotalCredits: null);

        var result = new Reconciler().Reconcile(statement);

        result.Sections.Single().ReconciliationStatus.Should().Be(ReconciliationStatus.Mismatch);
        result.ReconciliationStatus.Should().Be(ReconciliationStatus.Mismatch);
    }

    [Fact]
    public void Section_without_printed_subtotal_is_not_checked()
    {
        var statement = MakeStatement(
            sections:
            [
                MakeSection("2533", "X",
                    transactions: [Expense(10.00m)],
                    printedCharges: null, printedCredits: null),
            ],
            printedTotalCharges: null, printedTotalCredits: null);

        var result = new Reconciler().Reconcile(statement);

        result.Sections.Single().ReconciliationStatus.Should().Be(ReconciliationStatus.NotChecked);
    }

    private static Transaction Expense(decimal amount) => new()
    {
        TransactionDate = new DateOnly(2026, 5, 1),
        PostingDate = new DateOnly(2026, 5, 2),
        ReferenceNumber = "0",
        SequenceCode = "C000",
        RowType = RowType.Purchase,
        RawDescription = "x",
        Amount = amount,
        Direction = Direction.Expense,
        CardLast4 = "2533",
    };

    private static CardholderSection MakeSection(
        string last4, string name,
        IReadOnlyList<Transaction> transactions,
        decimal? printedCharges, decimal? printedCredits) => new()
        {
            CardLast4 = last4,
            RawName = name,
            Transactions = transactions,
            PrintedSubtotalCharges = printedCharges,
            PrintedSubtotalCredits = printedCredits,
        };

    private static Statement MakeStatement(
        IReadOnlyList<CardholderSection> sections,
        decimal? printedTotalCharges, decimal? printedTotalCredits) => new()
        {
            CardType = "TEST",
            MaskedAccount = "0000-00XX-XXXX-0000",
            Period = new StatementPeriod(new DateOnly(2026, 5, 21), new DateOnly(2026, 5, 18)),
            PageCount = 1,
            Sections = sections,
            PrintedTotalCharges = printedTotalCharges,
            PrintedTotalCredits = printedTotalCredits,
        };
}
