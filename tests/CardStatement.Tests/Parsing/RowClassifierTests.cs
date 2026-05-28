using CardStatement.Core.Models;
using CardStatement.Core.Parsing;
using CardStatement.Core.Pdf;
using FluentAssertions;

namespace CardStatement.Tests.Parsing;

public class RowClassifierTests
{
    private readonly RowClassifier _classifier = new();

    [Fact]
    public void Section_header_recognized_by_masked_card_pattern()
    {
        var row = MakeRow(("459378XXXXXX2533", 76.9), ("»»»", 171.96), ("CLAUDIA", 190.97));
        _classifier.Classify(row).Kind.Should().Be(ClassifiedRowKind.SectionHeader);
    }

    [Fact]
    public void Transaction_row_recognized_by_mmm_dd_first_token()
    {
        var row = MakeRow(("ABR/18", 38.91), ("19/04", 76.92), ("24816301", 105.44), ("C011", 148.2));
        _classifier.Classify(row).Kind.Should().Be(ClassifiedRowKind.Transaction);
    }

    [Fact]
    public void Subtotal_line_recognized()
    {
        var row = MakeRow(("SUBTOTAL.:", 171.96), ("$", 471.34), ("309.67", 495.1));
        _classifier.Classify(row).Kind.Should().Be(ClassifiedRowKind.SectionSubtotal);
    }

    [Fact]
    public void Total_line_recognized_by_dotted_continuation()
    {
        var row = MakeRow(("TOTAL", 171.96), ("...:", 200.48), ("$", 471.34), ("1,462.19", 485.6));
        _classifier.Classify(row).Kind.Should().Be(ClassifiedRowKind.StatementTotal);
    }

    [Theory]
    [InlineData("PUNTOS")]
    [InlineData("ASIGNADOS:")]
    [InlineData("BONIFICACION")]
    [InlineData("TRANSACCION")]
    public void Filter_lines_are_noise(string firstToken)
    {
        var row = MakeRow((firstToken, 50.0), ("X", 200.0));
        _classifier.Classify(row).Kind.Should().Be(ClassifiedRowKind.Noise);
    }

    private static TableRow MakeRow(params (string text, double x)[] words)
    {
        var pdfWords = words.Select(w => new PdfWord(1, w.text, w.x, 100.0, 10.0, 5.0)).ToList();
        return new TableRow(1, 100.0, pdfWords);
    }
}
