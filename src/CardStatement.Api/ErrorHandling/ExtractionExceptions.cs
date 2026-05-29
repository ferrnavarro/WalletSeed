namespace CardStatement.Api.ErrorHandling;

public sealed class NoTextExtractableException : Exception
{
    public NoTextExtractableException(string message) : base(message) { }
}

public sealed class UnrecognizedLayoutException : Exception
{
    public UnrecognizedLayoutException(string message) : base(message) { }
    public UnrecognizedLayoutException(string message, Exception innerException) : base(message, innerException) { }
}
