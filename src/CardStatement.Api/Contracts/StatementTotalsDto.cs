namespace CardStatement.Api.Contracts;

public sealed record StatementTotalsDto(
    decimal ComputedExpense,
    decimal ComputedIncome,
    decimal? PrintedExpense,
    decimal? PrintedIncome
);
