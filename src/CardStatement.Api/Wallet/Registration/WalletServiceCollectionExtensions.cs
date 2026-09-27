using Microsoft.Extensions.Options;

namespace CardStatement.Api.Wallet.Registration;

public static class WalletServiceCollectionExtensions
{
    public static IServiceCollection AddWalletIntegration(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<WalletOptions>(configuration.GetSection("Wallet"));

        services.AddHttpClient<IWalletApiClient, WalletApiClient>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<WalletOptions>>().Value;
            if (!string.IsNullOrWhiteSpace(options.BaseUrl) && Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUri))
            {
                client.BaseAddress = baseUri;
            }
            else
            {
                client.BaseAddress = new Uri("https://localhost");
            }

            client.Timeout = TimeSpan.FromSeconds(Math.Max(1, options.TimeoutSeconds));
        });

        services.AddScoped<LabelMappingResolver>();
        services.AddScoped<WalletImportService>();

        return services;
    }
}
