namespace CardStatement.Core.Models;

public sealed record Statement
{
    public required string CardType { get; init; }
    public required string MaskedAccount { get; init; }
    public required StatementPeriod Period { get; init; }
    public required int PageCount { get; init; }
    public IReadOnlyList<CardholderSection> Sections { get; init; } = [];
    public decimal? PrintedTotalCharges { get; init; }
    public decimal? PrintedTotalCredits { get; init; }
    public ReconciliationStatus ReconciliationStatus { get; init; } = ReconciliationStatus.NotChecked;
}
