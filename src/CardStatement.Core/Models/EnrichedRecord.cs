namespace CardStatement.Core.Models;

public sealed record EnrichedRecord
{
    public required DateOnly Date { get; init; }
    public required string Description { get; init; }
    public required Direction Direction { get; init; }
    public required decimal Amount { get; init; }
    public Guid? CategoryId { get; init; }
    public string? CategoryName { get; init; }
    public Guid? LabelId { get; init; }
    public string? LabelName { get; init; }
    public required string CardLast4 { get; init; }
    public bool NeedsReview { get; init; }
    public bool LabelUnmapped { get; init; }
}
