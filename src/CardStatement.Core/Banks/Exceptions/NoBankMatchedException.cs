using System;

namespace CardStatement.Core.Banks.Exceptions;

public sealed class NoBankMatchedException : Exception
{
    public NoBankMatchedException() : base("No registered bank recognized the uploaded PDF.") { }
}
