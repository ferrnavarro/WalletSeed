using CardStatement.Core.Models;

namespace CardStatement.Api.Contracts;

public sealed record TransactionDto(
    DateOnly Date,
    DateOnly PostingDate,
    string ReferenceNumber,
    string SequenceCode,
    RowType RowType,
    string Description,
    decimal Amount,
    Direction Direction,
    string CardLast4,
    bool NeedsReview,
    string? CategoryId,
    string? CategoryName,
    string? LabelId,
    string? LabelName,
    bool LabelUnmapped
);
