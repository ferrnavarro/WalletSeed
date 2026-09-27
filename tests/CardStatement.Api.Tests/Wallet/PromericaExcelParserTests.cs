using System.Text;
using CardStatement.Api.Wallet;

namespace CardStatement.Api.Tests.Wallet;

public class PromericaExcelParserTests
{
    private static readonly string SamplesDir = Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "promericaexcel");

    private static string? SampleFile()
    {
        var dir = Path.GetFullPath(SamplesDir);
        if (!Directory.Exists(dir)) return null;
        return Directory.GetFiles(dir, "*.xlsx").FirstOrDefault();
    }

    [Fact]
    public void Parse_SampleFile_ExtractsAllTransactions()
    {
        var filePath = SampleFile();
        if (filePath is null) return; // skip if samples missing

        using var stream = File.OpenRead(filePath);
        var result = PromericaExcelParser.Parse(stream);

        // Rows 9-47 in the sample sheet = 39 transactions
        Assert.Equal(39, result.Transactions.Count);
    }

    [Fact]
    public void Parse_SampleFile_UsesFechaOperacion_AndParsesAmounts()
    {
        var filePath = SampleFile();
        if (filePath is null) return;

        using var stream = File.OpenRead(filePath);
        var result = PromericaExcelParser.Parse(stream);

        // Row 9: Fecha operacion 07/09/2026, CARGO DE INTERESES, Creditos $0.00, Debitos $0.62
        var first = result.Transactions[0];
        Assert.Equal(new DateOnly(2026, 9, 7), first.Date);
        Assert.Equal("CARGO DE INTERESES", first.Description);
        Assert.Equal(0m, first.Creditos);
        Assert.Equal(0.62m, first.Debitos);
    }

    [Fact]
    public void Parse_SampleFile_ExtractsCreditAndPaymentRows()
    {
        var filePath = SampleFile();
        if (filePath is null) return;

        using var stream = File.OpenRead(filePath);
        var result = PromericaExcelParser.Parse(stream);

        // Row 47: "Pago", Creditos $2126.28
        var payment = result.Transactions.FirstOrDefault(t => t.Description == "Pago");
        Assert.NotNull(payment);
        Assert.Equal(2126.28m, payment!.Creditos);
        Assert.Equal(0m, payment.Debitos);

        // Row 46: interest discount, Creditos $24.85
        var discount = result.Transactions.FirstOrDefault(t => t.Description.StartsWith("Descuento de los intereses"));
        Assert.NotNull(discount);
        Assert.Equal(24.85m, discount!.Creditos);
    }

    [Fact]
    public void Parse_SampleFile_ExtractsCardLast4_PerRow()
    {
        var filePath = SampleFile();
        if (filePath is null) return;

        using var stream = File.OpenRead(filePath);
        var result = PromericaExcelParser.Parse(stream);

        Assert.Contains(result.Transactions, t => t.CardLast4 == "3326");
        Assert.Contains(result.Transactions, t => t.CardLast4 == "5106");
        // Tarjeta = "0" (account-level charges) → placeholder
        Assert.Contains(result.Transactions, t => t.CardLast4 == "0000");
    }

    [Fact]
    public void Parse_EmptyDescription_FallsBackToReference()
    {
        using var ms = new MemoryStream();
        using (var wb = new ClosedXML.Excel.XLWorkbook())
        {
            var ws = wb.AddWorksheet("Estado_de_cuenta");
            ws.Cell(1, 1).Value = "Fecha Movimiento";
            ws.Cell(1, 2).Value = "Referencia";
            ws.Cell(1, 3).Value = "Fecha operacion";
            ws.Cell(1, 4).Value = "Tarjeta";
            ws.Cell(1, 5).Value = "Descripcion";
            ws.Cell(1, 6).Value = "Creditos";
            ws.Cell(1, 7).Value = "Debitos";
            // Row with empty Descripcion
            ws.Cell(2, 1).Value = "04/09/2026 19:01:21";
            ws.Cell(2, 2).Value = "5690426926";
            ws.Cell(2, 3).Value = "06/09/2026 19:01:21";
            ws.Cell(2, 4).Value = "4589********3326";
            ws.Cell(2, 5).Value = "";
            ws.Cell(2, 6).Value = "$0.00";
            ws.Cell(2, 7).Value = "$17.37";
            wb.SaveAs(ms);
        }

        ms.Position = 0;
        var result = PromericaExcelParser.Parse(ms);

        var tx = Assert.Single(result.Transactions);
        Assert.Equal("Ref 5690426926", tx.Description);
        Assert.Equal(new DateOnly(2026, 9, 6), tx.Date);
        Assert.Equal(0m, tx.Creditos);
        Assert.Equal(17.37m, tx.Debitos);
        Assert.Equal("3326", tx.CardLast4);
    }

    [Fact]
    public void Parse_NoHeaderRow_Throws()
    {
        // Build a minimal xlsx without the expected header using ClosedXML
        using var ms = new MemoryStream();
        using (var wb = new ClosedXML.Excel.XLWorkbook())
        {
            var ws = wb.AddWorksheet("Sheet1");
            ws.Cell(1, 1).Value = "Something else";
            ws.Cell(2, 1).Value = "No transactions here";
            wb.SaveAs(ms);
        }

        ms.Position = 0;
        Assert.Throws<PromericaExcelParseException>(() => PromericaExcelParser.Parse(ms));
    }

    [Fact]
    public void Parse_HeaderButNoTransactions_Throws()
    {
        using var ms = new MemoryStream();
        using (var wb = new ClosedXML.Excel.XLWorkbook())
        {
            var ws = wb.AddWorksheet("Sheet1");
            ws.Cell(1, 1).Value = "Fecha Movimiento";
            ws.Cell(1, 2).Value = "Referencia";
            wb.SaveAs(ms);
        }

        ms.Position = 0;
        Assert.Throws<PromericaExcelParseException>(() => PromericaExcelParser.Parse(ms));
    }
}
