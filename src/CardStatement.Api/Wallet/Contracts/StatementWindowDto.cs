namespace CardStatement.Api.Wallet.Contracts;

public sealed record StatementWindowDto(DateOnly From, DateOnly To, DateOnly IssueDate, DateOnly CutoffDate);
