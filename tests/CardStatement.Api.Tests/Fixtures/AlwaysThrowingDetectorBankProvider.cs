using System;
using CardStatement.Core.Abstractions;
using CardStatement.Core.Banks;
using CardStatement.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace CardStatement.Api.Tests.Fixtures;

public sealed class AlwaysThrowingDetectorBankProvider : IBankProvider
{
    public BankInfo Info => new("broken", "Always-Throwing Bank");

    public BankDetection Detect(PdfDocumentWords words)
    {
        throw new InvalidOperationException("simulated bug");
    }

    public Statement Parse(PdfDocumentWords words)
    {
        throw new NotSupportedException("Parse should not be called on a provider that throws in Detect.");
    }
}

public static class AlwaysThrowingDetectorBankServiceCollectionExtensions
{
    public static IServiceCollection AddAlwaysThrowingDetectorBank(this IServiceCollection services)
    {
        services.AddSingleton<IBankProvider, AlwaysThrowingDetectorBankProvider>();
        return services;
    }
}
