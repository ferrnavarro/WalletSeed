using CardStatement.Core.Models;

namespace CardStatement.Api.Contracts;

public sealed record ExtractedStatementResponse(
    StatementHeaderDto Statement,
    IReadOnlyList<CardholderSectionDto> Sections,
    StatementTotalsDto Totals,
    ReconciliationStatus ReconciliationStatus,
    int NeedsReviewCount,
    IReadOnlyList<string> UnmappedCards,
    BankInfoDto Bank
);

