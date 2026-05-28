using CardStatement.Core.Models;

namespace CardStatement.Core.Abstractions;

public interface ICategorizer
{
    Task<IReadOnlyList<CategoryAssignment>> CategorizeAsync(
        IReadOnlyList<Transaction> transactions,
        CancellationToken ct = default);
}

public sealed record CategoryAssignment(Guid? CategoryId, string? CategoryName, bool NeedsReview);
