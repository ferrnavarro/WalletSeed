namespace CardStatement.Core.Models;

public sealed record CardholderSection
{
    public required string CardLast4 { get; init; }
    public required string RawName { get; init; }
    public Guid? LabelId { get; init; }
    public string? LabelName { get; init; }
    public IReadOnlyList<Transaction> Transactions { get; init; } = [];
    public decimal? PrintedSubtotalCharges { get; init; }
    public decimal? PrintedSubtotalCredits { get; init; }
    public ReconciliationStatus ReconciliationStatus { get; init; } = ReconciliationStatus.NotChecked;
}
