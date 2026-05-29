using CardStatement.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace CardStatement.Core.Banks.Bac;

public static class BacServiceCollectionExtensions
{
    public static IServiceCollection AddBacBank(this IServiceCollection services)
    {
        services.AddSingleton<IBankProvider, BacBankProvider>();
        return services;
    }
}
