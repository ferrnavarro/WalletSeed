using CardStatement.Core.Abstractions;
using CardStatement.Core.Models;

namespace CardStatement.Core.Categorization;

public sealed class StubLlmClient : ILlmClient
{
    public Task<IReadOnlyList<LlmCategoryChoice>> CategorizeBatchAsync(
        IReadOnlyList<LlmCategorizationItem> items,
        IReadOnlyList<Category> allowedCategories,
        CancellationToken ct = default)
    {
        IReadOnlyList<LlmCategoryChoice> result = items
            .Select(i => new LlmCategoryChoice(i.ItemId, CategoryId: null))
            .ToList();
        return Task.FromResult(result);
    }
}
