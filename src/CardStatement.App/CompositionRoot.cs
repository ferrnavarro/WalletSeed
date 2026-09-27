using CardStatement.App.Output;
using CardStatement.Core.Abstractions;
using CardStatement.Core.Apis;
using CardStatement.Core.Categorization;
using CardStatement.Core.Labels;
using CardStatement.Core.Models;
using CardStatement.Core.Registration;
using CardStatement.Core.Banks.Bac;
using CardStatement.Core.Result;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CardStatement.App;

public static class CompositionRoot
{
    public static IServiceCollection ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ApiOptions>(configuration.GetSection("Api"));
        services.Configure<CategorizationOptions>(configuration.GetSection("Categorization"));
        services.Configure<CardholderLabelOptions>(opt =>
        {
            var map = configuration.GetSection("CardholderLabels").Get<Dictionary<string, string>>() ?? [];
            foreach (var (k, v) in map)
            {
                if (Guid.TryParse(v, out var id))
                    opt.Map[k] = id;
            }
        });
        services.Configure<BacParsingOptions>(configuration.GetSection("Parsing"));

        services.AddTransient<BearerAuthHandler>();

        services.AddHttpClient<ICategoryApi, CategoryApiClient>((sp, http) =>
            {
                var apiOpts = sp.GetRequiredService<IOptions<ApiOptions>>().Value;
                http.BaseAddress = NormalizeBaseUrl(apiOpts.BaseUrl);
            })
            .AddHttpMessageHandler<BearerAuthHandler>();

        services.AddHttpClient<ILabelsApi, LabelApiClient>((sp, http) =>
            {
                var apiOpts = sp.GetRequiredService<IOptions<ApiOptions>>().Value;
                http.BaseAddress = NormalizeBaseUrl(apiOpts.BaseUrl);
            })
            .AddHttpMessageHandler<BearerAuthHandler>();

        services.AddCardStatementCore();
        services.AddBacBank();

        services.AddSingleton<ILlmClient>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<CategorizationOptions>>().Value;
            return opts.Provider?.ToLowerInvariant() switch
            {
                "openai" => new OpenAiLlmClient(opts.OpenAi, sp.GetService<ILogger<OpenAiLlmClient>>()),
                _ => new StubLlmClient(),
            };
        });

        services.AddSingleton<Pipeline>();
        services.AddSingleton<OutputWriter>();
        return services;
    }

    private static Uri NormalizeBaseUrl(string baseUrl)
    {
        var trimmed = baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/";
        return new Uri(trimmed);
    }
}
