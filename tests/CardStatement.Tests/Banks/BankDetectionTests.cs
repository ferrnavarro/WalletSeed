using System;
using CardStatement.Core.Banks;
using FluentAssertions;
using Xunit;

namespace CardStatement.Tests.Banks;

public sealed class BankDetectionTests
{
    [Fact]
    public void NoMatch_CreatesCorrectInstance()
    {
        // Act
        var detection = BankDetection.NoMatch("layout mismatch");

        // Assert
        detection.Matched.Should().BeFalse();
        detection.Confidence.Should().Be(0);
        detection.Reason.Should().Be("layout mismatch");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(50)]
    [InlineData(100)]
    public void Match_WithValidConfidence_CreatesCorrectInstance(int confidence)
    {
        // Act
        var detection = BankDetection.Match(confidence, "matched pattern");

        // Assert
        detection.Matched.Should().BeTrue();
        detection.Confidence.Should().Be(confidence);
        detection.Reason.Should().Be("matched pattern");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(101)]
    public void Constructor_WhenMatchedIsTrue_AndConfidenceOutOfRange_ThrowsArgumentOutOfRangeException(int confidence)
    {
        // Act
        Action act = () => new BankDetection(matched: true, confidence: confidence, reason: "test");

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    [InlineData(100)]
    public void Constructor_WhenMatchedIsFalse_AndConfidenceNotZero_ThrowsArgumentOutOfRangeException(int confidence)
    {
        // Act
        Action act = () => new BankDetection(matched: false, confidence: confidence, reason: "test");

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
