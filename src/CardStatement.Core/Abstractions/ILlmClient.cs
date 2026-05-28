using CardStatement.Core.Models;

namespace CardStatement.Core.Abstractions;

public interface ILlmClient
{
    Task<IReadOnlyList<LlmCategoryChoice>> CategorizeBatchAsync(
        IReadOnlyList<LlmCategorizationItem> items,
        IReadOnlyList<Category> allowedCategories,
        CancellationToken ct = default);
}

public sealed record LlmCategorizationItem(string ItemId, string Description, decimal Amount);

public sealed record LlmCategoryChoice(string ItemId, Guid? CategoryId);
