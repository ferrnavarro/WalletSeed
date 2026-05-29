namespace CardStatement.Api.Contracts;

public sealed record StatementPeriodDto(DateOnly IssueDate, DateOnly CutoffDate);
