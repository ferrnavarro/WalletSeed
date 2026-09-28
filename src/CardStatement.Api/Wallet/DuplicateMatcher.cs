namespace CardStatement.Api.Wallet;

public sealed record PdfRowInternal(
    int Index,
    DateOnly Date,
    decimal SignedAmount,
    string Currency,
    string Description,
    string? CounterParty,
    string CardholderSectionRawName,
    string CardLast4,
    string SourceKind = "card");

public static class DuplicateMatcher
{
    public static IReadOnlyDictionary<int, IReadOnlyList<string>> Match(
        IReadOnlyList<PdfRowInternal> pdfRows,
        IReadOnlyList<WalletRecord> walletRecords)
    {
        var result = new Dictionary<int, IReadOnlyList<string>>();
        foreach (var pdfRow in pdfRows.OrderBy(r => r.Index))
        {
            var matches = walletRecords
                .Where(record => Math.Abs((record.RecordDate.ToDateTime(new TimeOnly()) - pdfRow.Date.ToDateTime(new TimeOnly())).Days) <= 2)
                .Where(record => record.SignedAmount == pdfRow.SignedAmount)
                .Where(record => string.Equals(record.CurrencyCode, pdfRow.Currency, StringComparison.OrdinalIgnoreCase))
                .Where(record => Math.Sign(record.SignedAmount) == Math.Sign(pdfRow.SignedAmount))
                .Select(record => record.Id)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToList();

            result[pdfRow.Index] = matches;
        }

        return result;
    }
}
