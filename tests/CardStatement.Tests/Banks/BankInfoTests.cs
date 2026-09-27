using System;
using CardStatement.Core.Banks;
using FluentAssertions;
using Xunit;

namespace CardStatement.Tests.Banks;

public sealed class BankInfoTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Constructor_WhenIdIsInvalidString_ThrowsArgumentException(string? invalidId)
    {
        // Act
        Action act = () => new BankInfo(invalidId!, "BAC");

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Constructor_WhenDisplayNameIsInvalidString_ThrowsArgumentException(string? invalidDisplayName)
    {
        // Act
        Action act = () => new BankInfo("bac", invalidDisplayName!);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("BAC")] // Uppercase
    [InlineData("bac_id")] // Underscore
    [InlineData("-bac")] // Leading hyphen
    [InlineData("this-id-is-too-long-more-than-32-characters")] // Too long
    public void Constructor_WhenIdDoesNotMatchRegex_ThrowsArgumentException(string invalidId)
    {
        // Act
        Action act = () => new BankInfo(invalidId, "BAC");

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("bac")]
    [InlineData("banco-x")]
    [InlineData("bac-123")]
    public void Constructor_WithValidInputs_InitializesProperties(string validId)
    {
        // Act
        var bankInfo = new BankInfo(validId, "Valid Bank Name");

        // Assert
        bankInfo.Id.Should().Be(validId);
        bankInfo.DisplayName.Should().Be("Valid Bank Name");
    }
}
