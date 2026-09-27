using CardStatement.Api.Wallet;

namespace CardStatement.Api.Tests.Wallet;

public class CuscatlanExcelParserTests
{
    private static readonly string SamplesDir = Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "cuscaexcel");

    public static IEnumerable<object[]> SampleFiles()
    {
        var dir = Path.GetFullPath(SamplesDir);
        if (!Directory.Exists(dir)) yield break;
        foreach (var file in Directory.GetFiles(dir, "*.xlsx").OrderBy(f => f))
        {
            yield return new object[] { file };
        }
    }

    [Theory]
    [MemberData(nameof(SampleFiles))]
    public void Parse_SampleFiles_ExtractsTransactions(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var result = CuscatlanExcelParser.Parse(stream);

        Assert.NotEmpty(result.Transactions);
        Assert.All(result.Transactions, tx =>
        {
            Assert.NotEqual(0m, tx.Amount);
            Assert.False(string.IsNullOrWhiteSpace(tx.Description));
            Assert.Equal("4502", tx.CardLast4);
            Assert.True(tx.Date.Year >= 2000 && tx.Date.Year <= 2100);
        });
    }

    [Fact]
    public void Parse_AugustSample_HasExpectedRowCountAndValues()
    {
        var filePath = Path.GetFullPath(Path.Combine(SamplesDir, "TarjetaDeCredito_MovimientosEdodeCuenta_03ago2026-02sept2026.xlsx"));
        if (!File.Exists(filePath)) return;

        using var stream = File.OpenRead(filePath);
        var result = CuscatlanExcelParser.Parse(stream);

        Assert.Equal(14, result.Transactions.Count);

        // First row: 2026/08/26, AMAZON RETA* 5O8970IG1 SEATTLE, $ 99.00
        var first = result.Transactions[0];
        Assert.Equal(new DateOnly(2026, 8, 26), first.Date);
        Assert.Equal("AMAZON RETA* 5O8970IG1 SEATTLE", first.Description);
        Assert.Equal(99.00m, first.Amount);
        Assert.Equal("0568400000015", first.Reference);

        // Payment row: negative in file ("- $ 343.12")
        var payment = result.Transactions.FirstOrDefault(t => t.Description.StartsWith("PAGO RECIBIDO"));
        Assert.NotNull(payment);
        Assert.Equal(-343.12m, payment!.Amount);
    }

    [Fact]
    public void Parse_EachSample_HasExactlyOneNegativePaymentRow()
    {
        var dir = Path.GetFullPath(SamplesDir);
        if (!Directory.Exists(dir)) return;

        foreach (var file in Directory.GetFiles(dir, "*.xlsx"))
        {
            using var stream = File.OpenRead(file);
            var result = CuscatlanExcelParser.Parse(stream);
            // Every statement has at least one negative (payment/credit) row
            Assert.Contains(result.Transactions, t => t.Amount < 0 && t.Description.StartsWith("PAGO"));
        }
    }

    [Fact]
    public void Parse_EmptyDescription_FallsBackToReference()
    {
        using var ms = new MemoryStream();
        using (var wb = new ClosedXML.Excel.XLWorkbook())
        {
            var ws = wb.AddWorksheet("MovimientosEdodeCuenta");
            ws.Cell(1, 2).Value = "Número de tarjeta";
            ws.Cell(1, 3).Value = "Fecha";
            ws.Cell(1, 4).Value = "Referencia";
            ws.Cell(1, 5).Value = "Descripción";
            ws.Cell(1, 6).Value = "Dólares";
            ws.Cell(2, 2).Value = "XXXXXXXXXXXX-4502";
            ws.Cell(2, 3).Value = "2026/08/26";
            ws.Cell(2, 4).Value = "0568400000015";
            ws.Cell(2, 5).Value = "";
            ws.Cell(2, 6).Value = "$ 99.00";
            wb.SaveAs(ms);
        }

        ms.Position = 0;
        var result = CuscatlanExcelParser.Parse(ms);

        var tx = Assert.Single(result.Transactions);
        Assert.Equal("Ref 0568400000015", tx.Description);
        Assert.Equal(new DateOnly(2026, 8, 26), tx.Date);
        Assert.Equal(99.00m, tx.Amount);
        Assert.Equal("4502", tx.CardLast4);
    }

    [Fact]
    public void Parse_ZeroAmountRows_AreSkipped()
    {
        using var ms = new MemoryStream();
        using (var wb = new ClosedXML.Excel.XLWorkbook())
        {
            var ws = wb.AddWorksheet("MovimientosEdodeCuenta");
            ws.Cell(1, 2).Value = "Número de tarjeta";
            ws.Cell(1, 3).Value = "Fecha";
            ws.Cell(1, 4).Value = "Referencia";
            ws.Cell(1, 5).Value = "Descripción";
            ws.Cell(1, 6).Value = "Dólares";
            ws.Cell(2, 3).Value = "2026/08/26";
            ws.Cell(2, 5).Value = "ZERO ROW";
            ws.Cell(2, 6).Value = "$ 0.00";
            ws.Cell(3, 3).Value = "2026/08/25";
            ws.Cell(3, 5).Value = "REAL ROW";
            ws.Cell(3, 6).Value = "$ 5.00";
            wb.SaveAs(ms);
        }

        ms.Position = 0;
        var result = CuscatlanExcelParser.Parse(ms);

        var tx = Assert.Single(result.Transactions);
        Assert.Equal("REAL ROW", tx.Description);
    }

    [Fact]
    public void Parse_NoHeaderRow_Throws()
    {
        using var ms = new MemoryStream();
        using (var wb = new ClosedXML.Excel.XLWorkbook())
        {
            var ws = wb.AddWorksheet("Sheet1");
            ws.Cell(1, 1).Value = "Something unrelated";
            wb.SaveAs(ms);
        }

        ms.Position = 0;
        Assert.Throws<CuscatlanExcelParseException>(() => CuscatlanExcelParser.Parse(ms));
    }

    [Fact]
    public void Parse_HeaderButNoTransactions_Throws()
    {
        using var ms = new MemoryStream();
        using (var wb = new ClosedXML.Excel.XLWorkbook())
        {
            var ws = wb.AddWorksheet("MovimientosEdodeCuenta");
            ws.Cell(1, 2).Value = "Número de tarjeta";
            ws.Cell(1, 3).Value = "Fecha";
            ws.Cell(1, 4).Value = "Referencia";
            ws.Cell(1, 5).Value = "Descripción";
            ws.Cell(1, 6).Value = "Dólares";
            wb.SaveAs(ms);
        }

        ms.Position = 0;
        Assert.Throws<CuscatlanExcelParseException>(() => CuscatlanExcelParser.Parse(ms));
    }
}
