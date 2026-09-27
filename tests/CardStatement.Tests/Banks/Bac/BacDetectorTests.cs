using System;
using System.Collections.Generic;
using System.IO;
using CardStatement.Core.Banks;
using CardStatement.Core.Banks.Bac;
using CardStatement.Core.Models;
using CardStatement.Core.Pdf;
using CardStatement.Core.Abstractions;
using FluentAssertions;
using Xunit;

namespace CardStatement.Tests.Banks.Bac;

public sealed class BacDetectorTests
{
    private const string SamplePath = "../../../../../samples/final5140_45178439_316493_0.pdf";

    [Fact]
    public void Detect_WithSamplePdf_ReturnsHighConfidenceMatch()
    {
        // Arrange
        File.Exists(SamplePath).Should().BeTrue($"sample PDF must exist at {Path.GetFullPath(SamplePath)}");
        var words = new PdfPigExtractor().Extract(SamplePath);
        var detector = new BacDetector();

        // Act
        var result = detector.Detect(words);

        // Assert
        result.Matched.Should().BeTrue();
        result.Confidence.Should().Be(BankDetection.HighConfidence);
        result.Reason.Should().Contain("BIN 459378");
    }

    [Fact]
    public void Detect_WithEmptyWords_ReturnsNoMatch()
    {
        // Arrange
        var words = new PdfDocumentWords(1, Array.Empty<PdfWord>());
        var detector = new BacDetector();

        // Act
        var result = detector.Detect(words);

        // Assert
        result.Matched.Should().BeFalse();
        result.Confidence.Should().Be(0);
    }

    [Fact]
    public void Detect_WithHeaderOnlyNoBin_ReturnsMediumConfidenceMatch()
    {
        // Arrange
        // Trio same row: "CONCEPTO" + "CARGOS" + "ABONOS"
        var wordsList = new List<PdfWord>
        {
            new(1, "CONCEPTO", 100.0, 500.0, 10.0, 5.0),
            new(1, "CARGOS", 200.0, 500.0, 10.0, 5.0),
            new(1, "ABONOS", 550.0, 500.0, 10.0, 5.0)
        };
        var words = new PdfDocumentWords(1, wordsList);
        var detector = new BacDetector();

        // Act
        var result = detector.Detect(words);

        // Assert
        result.Matched.Should().BeTrue();
        result.Confidence.Should().Be(BankDetection.MediumConfidence);
        result.Reason.Should().Contain("without BIN");
    }
}
