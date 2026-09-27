using System;
using System.Collections.Generic;
using CardStatement.Core.Abstractions;
using CardStatement.Core.Banks;
using CardStatement.Core.Banks.Exceptions;
using CardStatement.Core.Models;
using FluentAssertions;
using Xunit;

namespace CardStatement.Tests.Banks;

public sealed class BankRegistryTests
{
    private sealed class FakeBankProvider : IBankProvider
    {
        public BankInfo Info { get; }
        public FakeBankProvider(string id, string name) => Info = new BankInfo(id, name);
        public BankDetection Detect(PdfDocumentWords words) => BankDetection.NoMatch();
        public Statement Parse(PdfDocumentWords words) => new Statement
        {
            CardType = "VISA",
            MaskedAccount = "1234",
            Period = new StatementPeriod(DateOnly.FromDateTime(DateTime.Today), DateOnly.FromDateTime(DateTime.Today)),
            PageCount = 0
        };
    }

    [Fact]
    public void Constructor_WhenEmpty_ThrowsEmptyBankRegistryException()
    {
        // Act
        Action act = () => new BankRegistry(new List<IBankProvider>());

        // Assert
        act.Should().Throw<EmptyBankRegistryException>();
    }

    [Fact]
    public void Constructor_WhenDuplicateId_ThrowsDuplicateBankIdException()
    {
        var providers = new List<IBankProvider>
        {
            new FakeBankProvider("bac", "BAC 1"),
            new FakeBankProvider("bac", "BAC 2"),
            new FakeBankProvider("banco-x", "Banco X")
        };

        // Act
        Action act = () => new BankRegistry(providers);

        // Assert
        var exception = act.Should().Throw<DuplicateBankIdException>().Which;
        exception.DuplicateIds.Should().ContainSingle().Which.Should().Be("bac");
    }

    [Fact]
    public void Constructor_InitializesSnapshotInRegistrationOrder()
    {
        var providers = new List<IBankProvider>
        {
            new FakeBankProvider("bac", "BAC"),
            new FakeBankProvider("banco-x", "Banco X")
        };

        // Act
        var registry = new BankRegistry(providers);

        // Assert
        registry.Providers.Should().HaveCount(2);
        registry.Providers[0].Info.Id.Should().Be("bac");
        registry.Providers[1].Info.Id.Should().Be("banco-x");
    }

    [Fact]
    public void Constructor_SnapshotsListAndIsImmutable()
    {
        var providers = new List<IBankProvider>
        {
            new FakeBankProvider("bac", "BAC")
        };

        var registry = new BankRegistry(providers);

        // Act
        providers.Add(new FakeBankProvider("banco-x", "Banco X"));

        // Assert
        registry.Providers.Should().HaveCount(1);
    }
}
