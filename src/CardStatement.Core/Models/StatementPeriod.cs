namespace CardStatement.Core.Models;

public sealed record StatementPeriod(DateOnly IssueDate, DateOnly CutoffDate);
