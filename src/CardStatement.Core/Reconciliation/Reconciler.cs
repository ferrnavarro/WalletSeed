using CardStatement.Core.Abstractions;
using CardStatement.Core.Models;

namespace CardStatement.Core.Reconciliation;

public sealed class Reconciler : IReconciler
{
    private const decimal Tolerance = 0.01m;

    public Statement Reconcile(Statement statement)
    {
        var reconciledSections = statement.Sections
            .Select(ReconcileSection)
            .ToList();

        var parsedTotalCharges = reconciledSections.Sum(s => s.Transactions
            .Where(t => t.Direction == Direction.Expense).Sum(t => t.Amount));
        var parsedTotalCredits = reconciledSections.Sum(s => s.Transactions
            .Where(t => t.Direction == Direction.Income).Sum(t => t.Amount));

        var totalStatus = CompareAggregate(
            parsedTotalCharges, statement.PrintedTotalCharges,
            parsedTotalCredits, statement.PrintedTotalCredits);

        var overallStatus = totalStatus is ReconciliationStatus.Mismatch
            || reconciledSections.Any(s => s.ReconciliationStatus == ReconciliationStatus.Mismatch)
                ? ReconciliationStatus.Mismatch
                : totalStatus;

        return statement with
        {
            Sections = reconciledSections,
            ReconciliationStatus = overallStatus,
        };
    }

    private static CardholderSection ReconcileSection(CardholderSection section)
    {
        var parsedCharges = section.Transactions
            .Where(t => t.Direction == Direction.Expense)
            .Sum(t => t.Amount);
        var parsedCredits = section.Transactions
            .Where(t => t.Direction == Direction.Income)
            .Sum(t => t.Amount);

        var status = CompareAggregate(
            parsedCharges, section.PrintedSubtotalCharges,
            parsedCredits, section.PrintedSubtotalCredits);

        return section with { ReconciliationStatus = status };
    }

    private static ReconciliationStatus CompareAggregate(
        decimal parsedCharges, decimal? printedCharges,
        decimal parsedCredits, decimal? printedCredits)
    {
        var anyChecked = false;
        var mismatch = false;

        if (printedCharges is decimal pc)
        {
            anyChecked = true;
            if (Math.Abs(parsedCharges - pc) > Tolerance) mismatch = true;
        }
        if (printedCredits is decimal pcr)
        {
            anyChecked = true;
            if (Math.Abs(parsedCredits - pcr) > Tolerance) mismatch = true;
        }

        if (!anyChecked) return ReconciliationStatus.NotChecked;
        return mismatch ? ReconciliationStatus.Mismatch : ReconciliationStatus.Ok;
    }
}
