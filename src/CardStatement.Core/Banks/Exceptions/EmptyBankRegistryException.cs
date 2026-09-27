using System;

namespace CardStatement.Core.Banks.Exceptions;

public sealed class EmptyBankRegistryException : Exception
{
    public EmptyBankRegistryException()
        : base("BankRegistry was constructed with zero IBankProvider implementations. " +
               "Did you forget to call services.AddBacBank() (or another bank's registration extension) in Program.cs?") { }
}
