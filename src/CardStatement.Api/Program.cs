using System.Text.Json;
using System.Text.Json.Serialization;
using CardStatement.Core.Abstractions;
using CardStatement.Core.Registration;
using CardStatement.Core.Banks.Bac;

using CardStatement.Api.Endpoints;
using CardStatement.Api.Contracts;

var builder = WebApplication.CreateBuilder(args);

// T016: Configure logging
builder.Logging.ClearProviders().AddSimpleConsole(o =>
{
    o.TimestampFormat = "[HH:mm:ss] ";
});
// R9 Logging Policy Constraint: PDF bytes and full transaction descriptions MUST NOT be logged at default level.

// Register CardStatement.Core services with DI
builder.Services.AddCardStatementCore();
builder.Services.AddBacBank();

// T013: Configure System.Text.Json options
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
});

// T014: Configure Kestrel max request body size limits
builder.WebHost.ConfigureKestrel(k =>
{
    k.Limits.MaxRequestBodySize = builder.Configuration.GetValue<long>("Kestrel:Limits:MaxRequestBodySize");
});

// T015: Register CORS with a named policy "frontend"
builder.Services.AddCors(options =>
{
    options.AddPolicy("frontend", policy =>
    {
        var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
        policy.WithOrigins(origins)
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

// Eagerly resolve BankRegistry to surface startup failures and log registered banks
var registry = app.Services.GetRequiredService<IBankRegistry>();
app.Logger.LogInformation("Registered banks: {Banks}", string.Join(", ", registry.Providers.Select(p => $"{p.Info.Id} ({p.Info.DisplayName})")));

app.UseExceptionHandler(exceptionHandlerApp =>
{
    exceptionHandlerApp.Run(async context =>
    {
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/json";

        var response = new ExtractionErrorResponse(
            new ErrorBody(ErrorCodes.ParseFailed, "Something went wrong while reading this PDF. Please try again.")
        );

        await context.Response.WriteAsJsonAsync(response);
    });
});

// Apply CORS policy
app.UseCors("frontend");

app.MapExtract();

app.MapGet("/", () => "WalletSeed Statement Extraction API is running.");

app.Run();

// Required to make the Program class accessible to WebApplicationFactory in integration tests
public partial class Program { }
