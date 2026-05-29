using System.Text.Json;
using System.Text.Json.Serialization;
using CardStatement.Core.Abstractions;
using CardStatement.Core.Parsing;
using CardStatement.Core.Pdf;
using CardStatement.Core.Reconciliation;

using CardStatement.Api.Endpoints;
using CardStatement.Api.Contracts;

var builder = WebApplication.CreateBuilder(args);

// T016: Configure logging
builder.Logging.ClearProviders().AddSimpleConsole(o =>
{
    o.TimestampFormat = "[HH:mm:ss] ";
});
// R9 Logging Policy Constraint: PDF bytes and full transaction descriptions MUST NOT be logged at default level.

// T012: Register CardStatement.Core services with DI
builder.Services.AddSingleton<IPdfExtractor, PdfPigExtractor>();
builder.Services.AddSingleton<IStatementParser, StatementParser>();
builder.Services.AddSingleton<IReconciler, Reconciler>();

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
