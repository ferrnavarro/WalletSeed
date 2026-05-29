using CardStatement.Core.Abstractions;
using CardStatement.Core.Banks;
using CardStatement.Core.Pdf;
using CardStatement.Core.Reconciliation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CardStatement.Core.Registration;

public static class CoreServiceCollectionExtensions
{
    public static IServiceCollection AddCardStatementCore(this IServiceCollection services)
    {
        services.TryAddSingleton<IPdfExtractor, PdfPigExtractor>();
        services.TryAddSingleton<IReconciler, Reconciler>();
        services.TryAddSingleton<IBankRegistry, BankRegistry>();
        services.TryAddSingleton<IBankResolver, BankResolver>();
        return services;
    }
}
