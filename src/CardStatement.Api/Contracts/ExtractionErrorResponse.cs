namespace CardStatement.Api.Contracts;

public sealed record ErrorBody(string Code, string Message);

public sealed record ExtractionErrorResponse(ErrorBody Error);
