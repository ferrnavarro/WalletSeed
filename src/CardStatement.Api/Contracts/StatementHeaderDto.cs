namespace CardStatement.Api.Contracts;

public sealed record StatementHeaderDto(
    string CardType,
    string MaskedAccount,
    StatementPeriodDto Period,
    int PageCount
);
