namespace CardStatement.Api.ErrorHandling;

public sealed class NoTextExtractableException : Exception
{
    public NoTextExtractableException(string message) : base(message) { }
}

