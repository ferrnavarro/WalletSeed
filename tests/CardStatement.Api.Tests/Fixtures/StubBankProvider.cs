using System;
using System.Linq;
using CardStatement.Core.Abstractions;
using CardStatement.Core.Banks;
using CardStatement.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace CardStatement.Api.Tests.Fixtures;

public sealed class StubBankProvider : IBankProvider
{
    public BankInfo Info => new("stub", "Stub Test Bank");

    public BankDetection Detect(PdfDocumentWords words)
    {
        var matched = words.Words.Any(w => string.Equals(w.Text, "__STUB_BANK__", StringComparison.Ordinal));
        return matched 
            ? BankDetection.Match(BankDetection.HighConfidence, "Found __STUB_BANK__ marker") 
            : BankDetection.NoMatch();
    }

    public Statement Parse(PdfDocumentWords words)
    {
        var transaction = new Transaction
        {
            TransactionDate = new DateOnly(2026, 5, 29),
            PostingDate = new DateOnly(2026, 5, 30),
            ReferenceNumber = "1234567890",
            SequenceCode = "0001",
            RowType = RowType.Purchase,
            RawDescription = "STUB TRANSACTION",
            Amount = 100.00m,
            Direction = Direction.Expense,
            CardLast4 = "9999",
            PageNumber = 1
        };

        var section = new CardholderSection
        {
            CardLast4 = "9999",
            RawName = "STUB USER",
            Transactions = new[] { transaction },
            PrintedSubtotalCharges = 100.00m,
            PrintedSubtotalCredits = 0.00m
        };

        return new Statement
        {
            CardType = "STUB CARD TYPE",
            MaskedAccount = "9999-XXXX-XXXX-9999",
            Period = new StatementPeriod(new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31)),
            PageCount = words.PageCount,
            Sections = new[] { section },
            PrintedTotalCharges = 100.00m,
            PrintedTotalCredits = 0.00m
        };
    }
}

public static class StubBankServiceCollectionExtensions
{
    public static IServiceCollection AddStubBank(this IServiceCollection services)
    {
        services.AddSingleton<IBankProvider, StubBankProvider>();
        return services;
    }
}
