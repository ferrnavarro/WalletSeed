using CardStatement.Core.Models;

namespace CardStatement.Api.Contracts;

public sealed record CardholderSectionDto(
    string CardLast4,
    string RawName,
    IReadOnlyList<TransactionDto> Transactions,
    SectionTotalsDto Totals,
    ReconciliationStatus ReconciliationStatus
);
