using System.Net.Http.Json;
using System.Text;
using CardStatement.Api.Tests;
using CardStatement.Api.Wallet.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CardStatement.Api.Tests.Wallet;

public class LoggingPrivacyAuditTests : IClassFixture<WebApiFactory>
{
    private readonly WebApiFactory _factory;

    public LoggingPrivacyAuditTests(WebApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task WalletSubmitAndCompare_DoNotLeakSensitiveDataInLogs()
    {
        var loggerProvider = new TestLoggerProvider();
        using var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<ILoggerProvider>(loggerProvider);
            });
        }).CreateClient();

        var payload = new SubmitRequest("acct-1", new[]
        {
            new SubmitRequestRow(0, new DateOnly(2026, 6, 10), -12.50m, "USD", "Coffee with secret", "Cafe", "MAIN", "cat-1")
        });

        var response = await client.PostAsJsonAsync("/api/wallet/import/submit", payload);
        Assert.NotEqual(System.Net.HttpStatusCode.OK, response.StatusCode);

        var logText = loggerProvider.GetLogText();
        Assert.DoesNotContain("jwt", logText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Coffee with secret", logText, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", logText, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class TestLoggerProvider : ILoggerProvider
    {
        private readonly StringBuilder _builder = new();

        public ILogger CreateLogger(string categoryName) => new TestLogger(_builder);

        public void Dispose() { }

        public string GetLogText() => _builder.ToString();
    }

    private sealed class TestLogger(StringBuilder builder) : ILogger
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            builder.AppendLine(formatter(state, exception));
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();
        public void Dispose() { }
    }
}
