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
    public void Parse_MultiCardSample_AssignsPerSectionCards()
    {
        var filePath = Path.GetFullPath(Path.Combine(SamplesDir, "multiplecards", "Estado de cuenta.csv"));
        if (!File.Exists(filePath)) return; // skip if samples missing

        using var stream = File.OpenRead(filePath);
        var result = BacCsvParser.Parse(stream);

        // Header card is the primary card
        Assert.Equal("5468", result.CardLast4);

        // All four section cards appear
        var cards = result.Transactions.Select(t => t.CardLast4).Distinct().OrderBy(c => c).ToList();
        Assert.Equal(new[] { "2533", "2640", "2706", "5468" }, cards);

        // Card 2640 section: 7 transactions (pet stores etc.)
        Assert.Equal(7, result.Transactions.Count(t => t.CardLast4 == "2640"));
        // Card 2706 section: 5 COMPASS transactions
        Assert.Equal(5, result.Transactions.Count(t => t.CardLast4 == "2706"));
        Assert.All(result.Transactions.Where(t => t.CardLast4 == "2706"), t => Assert.Contains("COMPASS", t.Description));

        // Payment row belongs to card 5468
        var payment = result.Transactions.FirstOrDefault(t => t.DollarsAmount < 0);
        Assert.NotNull(payment);
        Assert.Equal("5468", payment!.CardLast4);
        Assert.Equal(-742.19m, payment.DollarsAmount);

        // First row of the file belongs to the first section (2533)
        Assert.Equal("2533", result.Transactions[0].CardLast4);
        Assert.Equal("RESTAURANTE LA CABANA    SAN SALVADO", result.Transactions[0].Description);

        // Footer content excluded
        Assert.DoesNotContain(result.Transactions, t => t.Description.Contains("BONIFICACION", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.Transactions, t => t.Description.Contains("PUNTOS CREDOMATIC", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Parse_SingleCardSample_AllRowsShareHeaderCard()
    {
        var filePath = Path.GetFullPath(Path.Combine(SamplesDir, "Estado de cuenta.csv"));
        if (!File.Exists(filePath)) return;

        using var stream = File.OpenRead(filePath);
        var result = BacCsvParser.Parse(stream);

        Assert.All(result.Transactions, t => Assert.Equal("2127", t.CardLast4));
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
