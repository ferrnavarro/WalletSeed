using System;
using System.Collections.Generic;
using System.Linq;
using CardStatement.Api.ErrorHandling;
using CardStatement.Core.Abstractions;
using CardStatement.Core.Banks;
using CardStatement.Core.Banks.Exceptions;
using CardStatement.Core.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace CardStatement.Tests.Banks;

public sealed class BankResolverTests
{
    private sealed class TestLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Logs { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Logs.Add((logLevel, formatter(state, exception), exception));
        }
    }

    private sealed class FakeBankRegistry : IBankRegistry
    {
        public IReadOnlyList<IBankProvider> Providers { get; }
        public FakeBankRegistry(IReadOnlyList<IBankProvider> providers) => Providers = providers;
    }

    private sealed class StubBankProvider : IBankProvider
    {
        private readonly Func<PdfDocumentWords, BankDetection> _detectFunc;
        private readonly Func<PdfDocumentWords, Statement> _parseFunc;

        public BankInfo Info { get; }

        public StubBankProvider(string id, string name, Func<PdfDocumentWords, BankDetection> detectFunc, Func<PdfDocumentWords, Statement> parseFunc)
        {
            Info = new BankInfo(id, name);
            _detectFunc = detectFunc;
            _parseFunc = parseFunc;
        }

        public BankDetection Detect(PdfDocumentWords words) => _detectFunc(words);
        public Statement Parse(PdfDocumentWords words) => _parseFunc(words);
    }

    private static readonly PdfDocumentWords DummyWords = new(1, Array.Empty<PdfWord>());
    private static readonly Statement DummyStatement = new()
    {
        CardType = "VISA",
        MaskedAccount = "1234",
        Period = new StatementPeriod(DateOnly.FromDateTime(DateTime.Today), DateOnly.FromDateTime(DateTime.Today)),
        PageCount = 1
    };

    [Fact]
    public void Resolve_SingleMatch_ReturnsBankAndStatement()
    {
        // Arrange
        var logger = new TestLogger<BankResolver>();
        var provider = new StubBankProvider(
            "bac", "BAC",
            words => BankDetection.Match(BankDetection.HighConfidence, "matches BAC"),
            words => DummyStatement
        );
        var registry = new FakeBankRegistry(new[] { provider });
        var resolver = new BankResolver(registry, logger);

        // Act
        var result = resolver.Resolve(DummyWords);

        // Assert
        result.Bank.Id.Should().Be("bac");
        result.Statement.Should().Be(DummyStatement);
        logger.Logs.Should().Contain(l => l.Level == LogLevel.Information && l.Message.Contains("Bank selected: bac"));
    }

    [Fact]
    public void Resolve_NoMatch_ThrowsNoBankMatchedException()
    {
        // Arrange
        var logger = new TestLogger<BankResolver>();
        var provider = new StubBankProvider(
            "bac", "BAC",
            words => BankDetection.NoMatch("not BAC"),
            words => DummyStatement
        );
        var registry = new FakeBankRegistry(new[] { provider });
        var resolver = new BankResolver(registry, logger);

        // Act
        Action act = () => resolver.Resolve(DummyWords);

        // Assert
        act.Should().Throw<NoBankMatchedException>();
        logger.Logs.Should().Contain(l => l.Level == LogLevel.Information && l.Message.Contains("No bank matched"));
    }

    [Fact]
    public void Resolve_AmbiguousMatch_ReturnsHighestConfidence()
    {
        // Arrange
        var logger = new TestLogger<BankResolver>();
        var lowConfProvider = new StubBankProvider(
            "bac", "BAC",
            words => BankDetection.Match(BankDetection.LowConfidence, "low conf"),
            words => DummyStatement
        );
        var highConfProvider = new StubBankProvider(
            "banco-x", "Banco X",
            words => BankDetection.Match(BankDetection.HighConfidence, "high conf"),
            words => DummyStatement
        );
        var registry = new FakeBankRegistry(new[] { lowConfProvider, highConfProvider });
        var resolver = new BankResolver(registry, logger);

        // Act
        var result = resolver.Resolve(DummyWords);

        // Assert
        result.Bank.Id.Should().Be("banco-x");
        logger.Logs.Should().Contain(l => l.Level == LogLevel.Warning && l.Message.Contains("Ambiguous detection"));
    }

    [Fact]
    public void Resolve_AmbiguousMatchEqualConfidence_ReturnsLexicographicallySmallerId()
    {
        // Arrange
        var logger = new TestLogger<BankResolver>();
        var providerB = new StubBankProvider(
            "banco-y", "Banco Y",
            words => BankDetection.Match(BankDetection.HighConfidence, "same conf"),
            words => DummyStatement
        );
        var providerA = new StubBankProvider(
            "banco-x", "Banco X",
            words => BankDetection.Match(BankDetection.HighConfidence, "same conf"),
            words => DummyStatement
        );
        // Note: Register banco-y first to prove resolution order doesn't dictate result (D4)
        var registry = new FakeBankRegistry(new[] { providerB, providerA });
        var resolver = new BankResolver(registry, logger);

        // Act
        var result = resolver.Resolve(DummyWords);

        // Assert
        result.Bank.Id.Should().Be("banco-x");
        logger.Logs.Should().Contain(l => l.Level == LogLevel.Warning && l.Message.Contains("Ambiguous detection"));
    }

    [Fact]
    public void Resolve_DetectorThrows_IsContainedAndLogged()
    {
        // Arrange
        var logger = new TestLogger<BankResolver>();
        var brokenProvider = new StubBankProvider(
            "broken", "Broken Bank",
            words => throw new InvalidOperationException("detector failure"),
            words => DummyStatement
        );
        var workingProvider = new StubBankProvider(
            "working", "Working Bank",
            words => BankDetection.Match(BankDetection.HighConfidence, "matches working"),
            words => DummyStatement
        );
        var registry = new FakeBankRegistry(new[] { brokenProvider, workingProvider });
        var resolver = new BankResolver(registry, logger);

        // Act
        var result = resolver.Resolve(DummyWords);

        // Assert
        result.Bank.Id.Should().Be("working");
        logger.Logs.Should().Contain(l => l.Level == LogLevel.Error && l.Message.Contains("Bank detector 'broken' threw an exception"));
    }

    [Fact]
    public void Resolve_ParserThrows_IsWrappedAsUnrecognizedLayoutException()
    {
        // Arrange
        var logger = new TestLogger<BankResolver>();
        var innerException = new FormatException("bad layout format");
        var provider = new StubBankProvider(
            "bac", "BAC",
            words => BankDetection.Match(BankDetection.HighConfidence, "matches BAC"),
            words => throw innerException
        );
        var registry = new FakeBankRegistry(new[] { provider });
        var resolver = new BankResolver(registry, logger);

        // Act
        Action act = () => resolver.Resolve(DummyWords);

        // Assert
        var ex = act.Should().Throw<UnrecognizedLayoutException>().Which;
        ex.InnerException.Should().Be(innerException);
        logger.Logs.Should().Contain(l => l.Level == LogLevel.Warning && l.Message.Contains("Bank 'bac' could not parse the PDF"));
    }
}
