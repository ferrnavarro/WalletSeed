namespace CardStatement.Core.Models;

public sealed record Transaction
{
    public required DateOnly TransactionDate { get; init; }
    public required DateOnly PostingDate { get; init; }
    public required string ReferenceNumber { get; init; }
    public required string SequenceCode { get; init; }
    public required RowType RowType { get; init; }
    public required string RawDescription { get; init; }
    public required decimal Amount { get; init; }
    public required Direction Direction { get; init; }
    public required string CardLast4 { get; init; }
    public int PageNumber { get; init; }
}
