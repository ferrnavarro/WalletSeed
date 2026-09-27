using System.Text;
using CardStatement.Api.Wallet;

namespace CardStatement.Api.Tests.Wallet;

public class BacCsvParserTests
{
    private static readonly string SamplesDir = Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "baccsv");

    public static IEnumerable<object[]> SampleFiles()
    {
        var dir = Path.GetFullPath(SamplesDir);
        if (!Directory.Exists(dir)) yield break;
        foreach (var file in Directory.GetFiles(dir, "*.csv"))
        {
            yield return new object[] { file };
        }
    }

    [Theory]
    [MemberData(nameof(SampleFiles))]
    public void Parse_SampleFiles_ExtractsCardLast4AndTransactions(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var result = BacCsvParser.Parse(stream);

        Assert.Equal("2127", result.CardLast4);
        Assert.NotEmpty(result.Transactions);
        Assert.All(result.Transactions, tx =>
        {
            Assert.NotEqual(0m, tx.DollarsAmount);
            Assert.False(string.IsNullOrWhiteSpace(tx.Description));
            Assert.True(tx.Date.Year >= 2000 && tx.Date.Year <= 2100);
        });
    }

    [Fact]
    public void Parse_FirstSample_ExtractsExpectedRows()
    {
        var filePath = Path.GetFullPath(Path.Combine(SamplesDir, "Estado de cuenta.csv"));
        if (!File.Exists(filePath)) return; // skip if samples missing

        using var stream = File.OpenRead(filePath);
        var result = BacCsvParser.Parse(stream);

        Assert.Equal("2127", result.CardLast4);
        // First transaction: 25/07/2026, LA PAMPA ARGENTINA PASEO SAN SALVADO, 54.42
        var first = result.Transactions[0];
        Assert.Equal(new DateOnly(2026, 7, 25), first.Date);
        Assert.Equal("LA PAMPA ARGENTINA PASEO SAN SALVADO", first.Description);
        Assert.Equal(54.42m, first.DollarsAmount);

        // Payment row (negative): 18/08/2026, SU PAGO RECIBIDO GRACIAS, -425.61
        var payment = result.Transactions.FirstOrDefault(t => t.DollarsAmount < 0);
        Assert.NotNull(payment);
        Assert.Equal(new DateOnly(2026, 8, 18), payment!.Date);
        Assert.Equal(-425.61m, payment.DollarsAmount);

        // Footer content must be excluded
        Assert.DoesNotContain(result.Transactions, t => t.Description.Contains("Interest", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.Transactions, t => t.Description.Contains("Balance at cut-off", StringComparison.OrdinalIgnoreCase));
        // Zero-amount rows (LifeMiles etc.) must be excluded
        Assert.DoesNotContain(result.Transactions, t => t.Description.Contains("LifeMiles", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Parse_NoCardNumber_Throws()
    {
        var csv = "Date, , Local, Dollars\n01/01/2026, TEST, 0.00, 10.00\n";
        using var stream = new MemoryStream(Encoding.Latin1.GetBytes(csv));
        Assert.Throws<BacCsvParseException>(() => BacCsvParser.Parse(stream));
    }

    [Fact]
    public void Parse_NoTransactions_Throws()
    {
        var csv = "Header line\n4593-78**-****-2127, NAME, 27/08/2026\nDate, , Local, Dollars\n, Previous balance, 0.00, 0.00\n";
        using var stream = new MemoryStream(Encoding.Latin1.GetBytes(csv));
        Assert.Throws<BacCsvParseException>(() => BacCsvParser.Parse(stream));
    }
}
