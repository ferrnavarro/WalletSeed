using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using CardStatement.Api.Contracts;
using CardStatement.Api.Tests.Fixtures;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace CardStatement.Api.Tests;

public sealed class BrokenBankIsolationTests : IClassFixture<BrokenBankIsolationTests.Factory>
{
    public sealed class LogEntry
    {
        public LogLevel Level { get; set; }
        public string Message { get; set; } = string.Empty;
        public Exception? Exception { get; set; }
    }

    public sealed class MemoryLoggerProvider : ILoggerProvider
    {
        public ConcurrentBag<LogEntry> Logs { get; } = new();

        public ILogger CreateLogger(string categoryName) => new MemoryLogger(this);

        public void Dispose() { }

        private sealed class MemoryLogger : ILogger
        {
            private readonly MemoryLoggerProvider _provider;

            public MemoryLogger(MemoryLoggerProvider provider)
            {
                _provider = provider;
            }

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                _provider.Logs.Add(new LogEntry
                {
                    Level = logLevel,
                    Message = formatter(state, exception),
                    Exception = exception
                });
            }
        }
    }

    public sealed class Factory : WebApplicationFactory<Program>
    {
        public MemoryLoggerProvider LoggerProvider { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureLogging(logging =>
            {
                logging.AddProvider(LoggerProvider);
            });
            builder.ConfigureServices(services =>
            {
                services.AddAlwaysThrowingDetectorBank();
            });
        }
    }

    private readonly HttpClient _client;
    private readonly Factory _factory;
    private readonly JsonSerializerOptions _jsonOptions;

    public BrokenBankIsolationTests(Factory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
        _jsonOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    }

    [Fact]
    public async Task PostBacSamplePdf_WithBrokenBankRegistered_StillReturns200AndLogsError()
    {
        // Act
        using var content = new MultipartFormDataContent();
        using var fileStream = SamplePdf.OpenRead();
        using var streamContent = new StreamContent(fileStream);
        streamContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/pdf");
        content.Add(streamContent, "file", Path.GetFileName(SamplePdf.Path));

        var response = await _client.PostAsync("/api/statements/extract", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ExtractedStatementResponse>(_jsonOptions);
        result.Should().NotBeNull();
        result!.Bank.Id.Should().Be("bac");

        // Assert log entry has error level and contains 'broken' and 'simulated bug' / 'InvalidOperationException'
        _factory.LoggerProvider.Logs.Should().Contain(log =>
            log.Level == LogLevel.Error &&
            log.Message.Contains("broken") &&
            (log.Message.Contains("InvalidOperationException") || 
             log.Message.Contains("simulated bug") || 
             (log.Exception != null && log.Exception.GetType().Name.Contains("InvalidOperationException")))
        );
    }
}
