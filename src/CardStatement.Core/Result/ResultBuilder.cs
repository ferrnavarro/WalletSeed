using CardStatement.Core.Abstractions;
using CardStatement.Core.Models;

namespace CardStatement.Core.Result;

public sealed class ResultBuilder : IResultBuilder
{
    private readonly ILabelResolver _labels;
    private readonly ICategorizer _categorizer;

    public ResultBuilder(ILabelResolver labels, ICategorizer categorizer)
    {
        _labels = labels;
        _categorizer = categorizer;
    }

    public async Task<StatementResult> BuildAsync(Statement statement, CancellationToken ct = default)
    {
        var allTransactions = statement.Sections
            .SelectMany(s => s.Transactions)
            .ToList();

        var assignments = await _categorizer.CategorizeAsync(allTransactions, ct).ConfigureAwait(false);

        var sectionLabels = new Dictionary<string, LabelResolution>(StringComparer.Ordinal);
        foreach (var section in statement.Sections)
        {
            if (!sectionLabels.ContainsKey(section.CardLast4))
                sectionLabels[section.CardLast4] = await _labels.ResolveAsync(section.CardLast4, ct).ConfigureAwait(false);
        }

        var records = new List<EnrichedRecord>(allTransactions.Count);
        var unmappedCards = new Dictionary<string, string>(StringComparer.Ordinal);
        var needsReview = 0;
        var index = 0;

        foreach (var section in statement.Sections)
        {
            var label = sectionLabels[section.CardLast4];
            if (label.Unmapped && !unmappedCards.ContainsKey(section.CardLast4))
                unmappedCards[section.CardLast4] = section.RawName;

            foreach (var tx in section.Transactions)
            {
                var assignment = assignments[index++];
                if (assignment.NeedsReview) needsReview++;

                records.Add(new EnrichedRecord
                {
                    Date = tx.TransactionDate,
                    Description = tx.RawDescription,
                    Direction = tx.Direction,
                    Amount = tx.Amount,
                    CategoryId = assignment.CategoryId,
                    CategoryName = assignment.CategoryName,
                    LabelId = label.LabelId,
                    LabelName = label.LabelName,
                    CardLast4 = tx.CardLast4,
                    NeedsReview = assignment.NeedsReview,
                    LabelUnmapped = label.Unmapped,
                });
            }
        }

        var totalIncome = records.Where(r => r.Direction == Direction.Income).Sum(r => r.Amount);
        var totalExpense = records.Where(r => r.Direction == Direction.Expense).Sum(r => r.Amount);

        return new StatementResult
        {
            Statement = statement,
            Records = records,
            TotalIncome = totalIncome,
            TotalExpense = totalExpense,
            ReconciliationStatus = statement.ReconciliationStatus,
            UnmappedCards = unmappedCards.Select(kv => new UnmappedCard(kv.Key, kv.Value)).ToList(),
            NeedsReviewCount = needsReview,
        };
    }
}
