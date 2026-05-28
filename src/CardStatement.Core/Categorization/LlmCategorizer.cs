using CardStatement.Core.Abstractions;
using CardStatement.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CardStatement.Core.Categorization;

public sealed class LlmCategorizer : ICategorizer
{
    private readonly ILlmClient _llm;
    private readonly FixedCategoryResolver _fixed;
    private readonly IReadOnlyList<Category> _taxonomy;
    private readonly CategorizationOptions _options;
    private readonly ILogger<LlmCategorizer> _logger;

    public LlmCategorizer(
        ILlmClient llm,
        FixedCategoryResolver fixedResolver,
        IEnumerable<Category> taxonomy,
        CategorizationOptions options,
        ILogger<LlmCategorizer>? logger = null)
    {
        _llm = llm;
        _fixed = fixedResolver;
        _taxonomy = taxonomy.ToList();
        _options = options;
        _logger = logger ?? NullLogger<LlmCategorizer>.Instance;
    }

    public async Task<IReadOnlyList<CategoryAssignment>> CategorizeAsync(
        IReadOnlyList<Transaction> transactions,
        CancellationToken ct = default)
    {
        var assignments = new CategoryAssignment?[transactions.Count];
        var llmIndices = new List<int>();

        for (var i = 0; i < transactions.Count; i++)
        {
            var t = transactions[i];
            if (t.RowType == RowType.Purchase && t.Direction == Direction.Expense)
            {
                llmIndices.Add(i);
                continue;
            }

            var fixedCategory = _fixed.ResolveForFixedRow(t);
            assignments[i] = MakeAssignment(fixedCategory, needsReview: false);
        }

        var batchSize = Math.Max(1, _options.BatchSize);
        for (var start = 0; start < llmIndices.Count; start += batchSize)
        {
            var window = llmIndices.Skip(start).Take(batchSize).ToList();
            var items = window
                .Select((idx, n) => new LlmCategorizationItem(
                    ItemId: $"item_{start + n}",
                    Description: transactions[idx].RawDescription,
                    Amount: transactions[idx].Amount))
                .ToList();

            var choices = await _llm.CategorizeBatchAsync(items, _taxonomy, ct).ConfigureAwait(false);
            var byId = choices.ToDictionary(c => c.ItemId);

            for (var n = 0; n < window.Count; n++)
            {
                var itemId = $"item_{start + n}";
                var idx = window[n];
                Category? assigned = null;
                var needsReview = false;

                if (byId.TryGetValue(itemId, out var choice) && choice.CategoryId is Guid id)
                {
                    if (_fixed.IsAllowed(id))
                    {
                        assigned = _fixed.LookupById(id);
                    }
                    else
                    {
                        _logger.LogWarning("LLM returned id {Id} not in taxonomy for '{Desc}'. Falling back.",
                            id, transactions[idx].RawDescription);
                        assigned = _fixed.Fallback;
                        needsReview = true;
                    }
                }
                else
                {
                    assigned = _fixed.Fallback;
                    needsReview = _options.FlagFallbackAsNeedsReview;
                }

                assignments[idx] = MakeAssignment(assigned, needsReview);
            }
        }

        return assignments.Select(a => a ?? new CategoryAssignment(null, null, NeedsReview: true)).ToList();
    }

    private static CategoryAssignment MakeAssignment(Category? category, bool needsReview)
    {
        if (category is null)
            return new CategoryAssignment(null, null, NeedsReview: true);
        return new CategoryAssignment(category.Id, category.Name, needsReview);
    }
}
