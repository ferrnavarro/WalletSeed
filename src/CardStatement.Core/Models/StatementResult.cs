namespace CardStatement.Core.Models;

public sealed record StatementResult
{
    public required Statement Statement { get; init; }
    public required IReadOnlyList<EnrichedRecord> Records { get; init; }
    public required decimal TotalIncome { get; init; }
    public required decimal TotalExpense { get; init; }
    public required ReconciliationStatus ReconciliationStatus { get; init; }
    public required IReadOnlyList<UnmappedCard> UnmappedCards { get; init; }
    public int NeedsReviewCount { get; init; }
}

public sealed record UnmappedCard(string CardLast4, string RawName);
