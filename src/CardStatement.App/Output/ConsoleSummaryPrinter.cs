using System.Globalization;
using CardStatement.Core.Models;

namespace CardStatement.App.Output;

public static class ConsoleSummaryPrinter
{
    public static void Print(StatementResult result)
    {
        var s = result.Statement;
        Console.WriteLine($"Card         : {s.CardType}");
        Console.WriteLine($"Account      : {s.MaskedAccount}");
        Console.WriteLine($"Period       : {s.Period.IssueDate:yyyy-MM-dd} → {s.Period.CutoffDate:yyyy-MM-dd}");
        Console.WriteLine($"Pages        : {s.PageCount}");
        Console.WriteLine($"Reconcile    : {result.ReconciliationStatus}");
        Console.WriteLine();

        Console.WriteLine("Sections:");
        foreach (var sec in s.Sections)
        {
            var label = result.Records.FirstOrDefault(r => r.CardLast4 == sec.CardLast4)?.LabelName ?? "(unmapped)";
            var sectionExpense = sec.Transactions.Where(t => t.Direction == Direction.Expense).Sum(t => t.Amount);
            var sectionIncome = sec.Transactions.Where(t => t.Direction == Direction.Income).Sum(t => t.Amount);
            Console.WriteLine(
                $"  {sec.CardLast4}  {sec.RawName,-25}  label={label,-25}  txns={sec.Transactions.Count,3}  expense={sectionExpense,10:N2}  income={sectionIncome,10:N2}  [{sec.ReconciliationStatus}]");
        }

        Console.WriteLine();
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Total income  : {result.TotalIncome:N2}"));
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Total expense : {result.TotalExpense:N2}"));

        if (s.PrintedTotalCharges is decimal pc)
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Printed TOTAL : charges={pc:N2}  credits={s.PrintedTotalCredits:N2}"));

        Console.WriteLine();
        Console.WriteLine($"NeedsReview   : {result.NeedsReviewCount}");
        if (result.UnmappedCards.Count > 0)
        {
            Console.WriteLine("Unmapped cards (add to CardholderLabels):");
            foreach (var u in result.UnmappedCards)
                Console.WriteLine($"  {u.CardLast4}  {u.RawName}");
        }
    }
}
