using CardStatement.Api.Wallet;

namespace CardStatement.Tests.Wallet;

public class DuplicateMatcherTests
{
    [Fact]
    public void Match_ReturnsExpectedPairings_ForMatchingAndNonMatchingRows()
    {
        var pdfRows = new List<PdfRowInternal>
        {
            new(0, new DateOnly(2026, 6, 10), -25m, "USD", "Coffee", null, "MAIN", "1234"),
            new(1, new DateOnly(2026, 6, 12), 100m, "USD", "Salary", null, "MAIN", "1234"),
            new(2, new DateOnly(2026, 6, 15), -10m, "USD", "Taxi", null, "MAIN", "1234")
        };

        var walletRecords = new List<WalletRecord>
        {
            new("wr-1", new DateOnly(2026, 6, 11), -25m, "USD", "Coffee", null, "Food"),
            new("wr-2", new DateOnly(2026, 6, 12), 100m, "USD", "Salary", null, "Income"),
            new("wr-3", new DateOnly(2026, 6, 18), -10m, "USD", "Taxi", null, "Transport")
        };

        var match = DuplicateMatcher.Match(pdfRows, walletRecords);

        Assert.Equal(3, match.Count);
        Assert.Equal(new[] { "wr-1" }, match[0]);
        Assert.Equal(new[] { "wr-2" }, match[1]);
        Assert.Empty(match[2]);
    }
}
