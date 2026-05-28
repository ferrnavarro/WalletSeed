using CardStatement.Core.Models;
using CardStatement.Core.Parsing;
using CardStatement.Core.Pdf;
using FluentAssertions;

namespace CardStatement.Tests.Parsing;

public class TransactionRowParserTests
{
    private static readonly StatementPeriod Period = new(
        new DateOnly(2026, 5, 21), new DateOnly(2026, 5, 18));
    private static readonly ColumnBands Bands = new(
        TransactionDateX: 38.9, PostingDateX: 76.9, ReferenceX: 105.4,
        SequenceX: 148.2, DescriptionLeftX: 171.9, DescriptionRightX: 460.0,
        CargosAbonosSplitX: 520.0, PageRightX: 600.0);

    private static TransactionRowParser MakeParser() =>
        new(new TransactionDateResolver(Period), Bands);

    [Fact]
    public void Parses_purchase_row_with_amount_in_cargos_column()
    {
        var row = MakeRow(
            ("ABR/18", 38.91), ("19/04", 76.92), ("24816301", 105.44), ("C011", 148.2),
            ("BURGER", 171.96), ("KING", 205.23), ("AHUACHAPAN", 228.99),
            ("$", 471.34), ("2.00", 504.6));

        var tx = MakeParser().Parse(row, "2533");

        tx.Direction.Should().Be(Direction.Expense);
        tx.RowType.Should().Be(RowType.Purchase);
        tx.SequenceCode.Should().Be("C011");
        tx.ReferenceNumber.Should().Be("24816301");
        tx.Amount.Should().Be(2.00m);
        tx.RawDescription.Should().Be("BURGER KING AHUACHAPAN");
        tx.CardLast4.Should().Be("2533");
    }

    [Fact]
    public void Direction_is_set_by_amount_column_not_merchant()
    {
        var row = MakeRow(
            ("MAY/15", 38.91), ("15/05", 76.92), ("00094000", 105.44), ("X504", 148.2),
            ("REVERSION", 171.96), ("PLAN", 219.48), ("PRF", 243.24), ("T.ADI", 290.76),
            ("$", 533.12), ("75.00", 561.63));

        var tx = MakeParser().Parse(row, "4941");

        tx.Direction.Should().Be(Direction.Income);
        tx.RowType.Should().Be(RowType.Financing);
        tx.Amount.Should().Be(75.00m);
    }

    [Fact]
    public void Handles_merged_reference_sequence_in_payment_row()
    {
        var row = MakeRow(
            ("ABR/24", 38.91), ("24/04", 76.92), ("000009496P155", 105.44),
            ("SU", 171.96), ("PAGO", 186.22), ("RECIBIDO", 209.98), ("GRACIAS", 252.75),
            ("$", 533.12), ("802.01", 556.88));

        var tx = MakeParser().Parse(row, "5468");

        tx.ReferenceNumber.Should().Be("000009496");
        tx.SequenceCode.Should().Be("P155");
        tx.RowType.Should().Be(RowType.Payment);
        tx.Direction.Should().Be(Direction.Income);
        tx.Amount.Should().Be(802.01m);
        tx.RawDescription.Should().Be("SU PAGO RECIBIDO GRACIAS");
    }

    [Fact]
    public void Preserves_collided_branch_text_in_description()
    {
        var row = MakeRow(
            ("ABR/19", 38.91), ("21/04", 76.92), ("00000605", 105.44), ("C798", 148.2),
            ("BURGER", 171.96), ("KING", 205.23), ("AVE.", 228.99),
            ("MASFERRESAN", 252.75), ("S", 309.77),
            ("$", 471.34), ("2.80", 504.6));

        var tx = MakeParser().Parse(row, "2533");

        tx.RawDescription.Should().Be("BURGER KING AVE. MASFERRESAN S");
    }

    private static TableRow MakeRow(params (string text, double x)[] words)
    {
        var pdfWords = words.Select(w => new PdfWord(1, w.text, w.x, 10.0, 10.0, 5.0)).ToList();
        return new TableRow(1, 100.0, pdfWords);
    }
}
