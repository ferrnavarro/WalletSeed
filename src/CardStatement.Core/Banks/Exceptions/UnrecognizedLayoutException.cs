using System;

namespace CardStatement.Api.ErrorHandling;

public sealed class UnrecognizedLayoutException : Exception
{
    public UnrecognizedLayoutException(string message) : base(message) { }
    public UnrecognizedLayoutException(string message, Exception innerException) : base(message, innerException) { }
}
