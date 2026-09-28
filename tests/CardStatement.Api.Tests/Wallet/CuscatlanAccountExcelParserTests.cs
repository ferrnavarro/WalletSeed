using CardStatement.Api.Wallet;

namespace CardStatement.Api.Tests.Wallet;

public class CuscatlanAccountExcelParserTests
{
    private static readonly string SamplesDir = Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "bankaccounts", "cusca");

    private static string SampleFile()
    {
        return Path.GetFullPath(Path.Combine(SamplesDir, "Cuenta_Detalledemovimientos_28sept2026.xlsx"));
    }

    [Fact]
    public void Parse_SampleFile_ExtractsAllMovements()
    {
        var filePath = SampleFile();
        if (!File.Exists(filePath)) return;

        using var stream = File.OpenRead(filePath);
        var result = CuscatlanAccountExcelParser.Parse(stream);

        Assert.Equal(34, result.Transactions.Count);
        Assert.All(result.Transactions, tx =>
        {
            Assert.NotEqual(0m, tx.SignedAmount);
            Assert.False(string.IsNullOrWhiteSpace(tx.Description));
            Assert.Equal("2037", tx.AccountLast4);
            Assert.Equal("USD", tx.Currency);
            Assert.True(tx.Date.Year >= 2000 && tx.Date.Year <= 2100);
        });
    }

    [Fact]
    public void Parse_SampleFile_HasExpectedRowValues()
    {
        var filePath = SampleFile();
        if (!File.Exists(filePath)) return;

        using var stream = File.OpenRead(filePath);
        var result = CuscatlanAccountExcelParser.Parse(stream);

        // Newest first: 26/09/2026 Pago De Tarjeta De Credito, - $ 62.75, ref 1004091988
        var first = result.Transactions[0];
        Assert.Equal(new DateOnly(2026, 9, 26), first.Date);
        Assert.Equal("Pago De Tarjeta De Credito", first.Description);
        Assert.Equal(-62.75m, first.SignedAmount);
        Assert.Equal("1004091988", first.Reference);

        // Income row: 16/09/2026 Abono Transfer365 Pagos Recibido, $ 700.00
        var income = result.Transactions.FirstOrDefault(t => t.Description.StartsWith("Abono Transfer365"));
        Assert.NotNull(income);
        Assert.Equal(700.00m, income!.SignedAmount);
        Assert.Equal(new DateOnly(2026, 9, 16), income.Date);

        // Outflows are negative, inflows positive (Wallet convention is preserved as-is)
        Assert.Contains(result.Transactions, t => t.SignedAmount < 0);
        Assert.Contains(result.Transactions, t => t.SignedAmount > 0);
    }

    [Fact]
    public void Parse_SampleFile_ParsedAmountsTrackTheReportedRunningBalance()
    {
        // Guards the sign convention: walking the movements oldest → newest with the
        // parsed signed amounts must reproduce the "Saldo disponible" column.
        var filePath = SampleFile();
        if (!File.Exists(filePath)) return;

        using var stream = File.OpenRead(filePath);
        var result = CuscatlanAccountExcelParser.Parse(stream);

        // The export lists movements newest-first; reversing gives chronological order
        // (within a date, the last-listed row happened first).
        var ordered = Enumerable.Reverse(result.Transactions).ToList();

        // Known balances from the export: the oldest movement (06/07/2026
        // Pago De Tarjeta De Credito -812.42) leaves "Saldo disponible" at $ 887.19.
        var balance = 887.19m;
        foreach (var tx in ordered.Skip(1))
        {
            balance += tx.SignedAmount;

            // Spot-check a few rows against the export's "Saldo disponible" column.
            if (tx.Date == new DateOnly(2026, 7, 26) && tx.SignedAmount == -5.50m)
            {
                Assert.Equal(252.20m, balance);
            }

            if (tx.Date == new DateOnly(2026, 9, 16) && tx.SignedAmount == 700.00m)
            {
                Assert.Equal(1337.17m, balance);
            }
        }

        // Final balance after the newest movement (26/09/2026 -62.75) → $ 267.42
        Assert.Equal(267.42m, balance);
    }

    [Fact]
    public void Parse_SampleFile_IsDetectedAsAccountLayoutNotCreditCard()
    {
        var filePath = SampleFile();
        if (!File.Exists(filePath)) return;

        using var stream = File.OpenRead(filePath);
        // The credit-card parser must reject it (no "Dólares" column)…
        Assert.Throws<CuscatlanExcelParseException>(() => CuscatlanExcelParser.Parse(stream));

        // …while the auto-detector routes it to the account layout.
        stream.Position = 0;
        var result = BankExcelParser.Parse(stream);
        Assert.All(result, tx =>
        {
            Assert.Equal("account", tx.SourceKind);
            Assert.Equal("2037", tx.CardLast4);
            Assert.Equal("USD", tx.Currency);
        });

        // Signs pass through unchanged: the oldest movement is an outflow.
        var oldest = Enumerable.Reverse(result).First();
        Assert.Equal(-812.42m, oldest.SignedAmount);
    }

    [Fact]
    public void Parse_ColonAmounts_AreParsedAsColones()
    {
        using var ms = new MemoryStream();
        using (var wb = new ClosedXML.Excel.XLWorkbook())
        {
            var ws = wb.AddWorksheet("Cuenta");
            ws.Cell(1, 2).Value = "No. de cuenta: ";
            ws.Cell(1, 3).Value = "XXXXXXXXXXX-2037";
            ws.Cell(3, 2).Value = "Fecha";
            ws.Cell(3, 3).Value = "Detalles de movimientos";
            ws.Cell(3, 4).Value = "Monto";
            ws.Cell(3, 5).Value = "Saldo disponible";
            ws.Cell(3, 6).Value = "Referencia";
            ws.Cell(4, 2).Value = "26/09/2026";
            ws.Cell(4, 3).Value = "Abono";
            ws.Cell(4, 4).Value = "₡ 1 234,56";
            ws.Cell(4, 6).Value = "12345";
            wb.SaveAs(ms);
        }

        ms.Position = 0;
        var result = CuscatlanAccountExcelParser.Parse(ms);

        var tx = Assert.Single(result.Transactions);
        Assert.Equal(1234.56m, tx.SignedAmount);
        Assert.Equal("CRC", tx.Currency);
    }

    [Fact]
    public void Parse_NumericDateAndAmountCells_AreParsed()
    {
        using var ms = new MemoryStream();
        using (var wb = new ClosedXML.Excel.XLWorkbook())
        {
            var ws = wb.AddWorksheet("Cuenta");
            ws.Cell(2, 2).Value = "Fecha";
            ws.Cell(2, 3).Value = "Detalles de movimientos";
            ws.Cell(2, 4).Value = "Monto";
            ws.Cell(3, 2).Value = new DateTime(2026, 7, 8);
            ws.Cell(3, 3).Value = "Movimiento";
            ws.Cell(3, 4).Value = -15.0;
            wb.SaveAs(ms);
        }

        ms.Position = 0;
        var result = CuscatlanAccountExcelParser.Parse(ms);

        var tx = Assert.Single(result.Transactions);
        Assert.Equal(new DateOnly(2026, 7, 8), tx.Date);
        Assert.Equal(-15.00m, tx.SignedAmount);
        Assert.Equal("0000", tx.AccountLast4); // no account metadata
    }

    [Fact]
    public void Parse_EmptyDescription_FallsBackToReference()
    {
        using var ms = new MemoryStream();
        using (var wb = new ClosedXML.Excel.XLWorkbook())
        {
            var ws = wb.AddWorksheet("Cuenta");
            ws.Cell(2, 2).Value = "Fecha";
            ws.Cell(2, 3).Value = "Detalles de movimientos";
            ws.Cell(2, 4).Value = "Monto";
            ws.Cell(2, 6).Value = "Referencia";
            ws.Cell(3, 2).Value = "26/09/2026";
            ws.Cell(3, 3).Value = "";
            ws.Cell(3, 4).Value = "$ 10.00";
            ws.Cell(3, 6).Value = "999888777";
            wb.SaveAs(ms);
        }

        ms.Position = 0;
        var result = CuscatlanAccountExcelParser.Parse(ms);

        var tx = Assert.Single(result.Transactions);
        Assert.Equal("Ref 999888777", tx.Description);
    }

    [Fact]
    public void Parse_ZeroAmountRows_AreSkipped()
    {
        using var ms = new MemoryStream();
        using (var wb = new ClosedXML.Excel.XLWorkbook())
        {
            var ws = wb.AddWorksheet("Cuenta");
            ws.Cell(2, 2).Value = "Fecha";
            ws.Cell(2, 3).Value = "Detalles de movimientos";
            ws.Cell(2, 4).Value = "Monto";
            ws.Cell(3, 2).Value = "26/09/2026";
            ws.Cell(3, 3).Value = "ZERO ROW";
            ws.Cell(3, 4).Value = "$ 0.00";
            ws.Cell(4, 2).Value = "25/09/2026";
            ws.Cell(4, 3).Value = "REAL ROW";
            ws.Cell(4, 4).Value = "- $ 5.00";
            wb.SaveAs(ms);
        }

        ms.Position = 0;
        var result = CuscatlanAccountExcelParser.Parse(ms);

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
        Assert.Throws<CuscatlanAccountExcelParseException>(() => CuscatlanAccountExcelParser.Parse(ms));
    }

    [Fact]
    public void Parse_HeaderButNoTransactions_Throws()
    {
        using var ms = new MemoryStream();
        using (var wb = new ClosedXML.Excel.XLWorkbook())
        {
            var ws = wb.AddWorksheet("Cuenta");
            ws.Cell(2, 2).Value = "Fecha";
            ws.Cell(2, 3).Value = "Detalles de movimientos";
            ws.Cell(2, 4).Value = "Monto";
            wb.SaveAs(ms);
        }

        ms.Position = 0;
        Assert.Throws<CuscatlanAccountExcelParseException>(() => CuscatlanAccountExcelParser.Parse(ms));
    }
}
