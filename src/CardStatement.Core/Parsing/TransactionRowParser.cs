using System.Text.RegularExpressions;
using CardStatement.Core.Models;
using CardStatement.Core.Pdf;

namespace CardStatement.Core.Parsing;

public sealed partial class TransactionRowParser
{
    [GeneratedRegex(@"^(?<ref>\d+)(?<seq>[A-Z]\d+)$", RegexOptions.CultureInvariant)]
    private static partial Regex MergedRefSeqRegex();

    [GeneratedRegex(@"^[A-Z]\d+$", RegexOptions.CultureInvariant)]
    private static partial Regex SequenceCodeRegex();

    private readonly TransactionDateResolver _dates;
    private readonly ColumnBands _bands;

    public TransactionRowParser(TransactionDateResolver dates, ColumnBands bands)
    {
        _dates = dates;
        _bands = bands;
    }

    public Transaction Parse(TableRow row, string cardLast4)
    {
        if (row.Words.Count < 4)
            throw new FormatException($"Transaction row too short: {DebugText(row)}");

        var w = row.Words;
        var txnDate = _dates.ResolveTransactionDate(w[0].Text);
        var postDate = _dates.ResolvePostingDate(w[1].Text);

        string reference;
        string sequence;
        var bodyStartIdx = 3;

        if (SequenceCodeRegex().IsMatch(w[3].Text))
        {
            reference = w[2].Text;
            sequence = w[3].Text;
            bodyStartIdx = 4;
        }
        else
        {
            var merged = MergedRefSeqRegex().Match(w[2].Text);
            if (!merged.Success)
                throw new FormatException($"Cannot identify reference/sequence in: {DebugText(row)}");
            reference = merged.Groups["ref"].Value;
            sequence = merged.Groups["seq"].Value;
            bodyStartIdx = 3;
        }

        var amountIdx = FindAmountIndex(w);
        if (amountIdx < bodyStartIdx)
            throw new FormatException($"No amount in row: {DebugText(row)}");

        var descWords = w.Skip(bodyStartIdx).Take(amountIdx - bodyStartIdx)
            .Where(t => t.Text != "$")
            .Select(t => t.Text);
        var rawDescription = string.Join(" ", descWords).Trim();

        var amountToken = w[amountIdx];
        var amount = AmountParser.Parse(amountToken.Text);
        var direction = amountToken.X < _bands.CargosAbonosSplitX ? Direction.Expense : Direction.Income;
        var rowType = ClassifyRowType(sequence);

        return new Transaction
        {
            TransactionDate = txnDate,
            PostingDate = postDate,
            ReferenceNumber = reference,
            SequenceCode = sequence,
            RowType = rowType,
            RawDescription = rawDescription,
            Amount = amount,
            Direction = direction,
            CardLast4 = cardLast4,
            PageNumber = row.PageNumber,
        };
    }

    private static int FindAmountIndex(IReadOnlyList<PdfWord> words)
    {
        for (var i = words.Count - 1; i >= 0; i--)
        {
            if (AmountParser.TryParse(words[i].Text, out _))
                return i;
        }
        return -1;
    }

    private static RowType ClassifyRowType(string sequence)
    {
        if (sequence.Length == 0) return RowType.Adjustment;
        return sequence[0] switch
        {
            'C' => RowType.Purchase,
            'X' => RowType.Financing,
            'P' => RowType.Payment,
            _ => RowType.Adjustment,
        };
    }

    private static string DebugText(TableRow row) =>
        $"p{row.PageNumber} y={row.Y:0.00} [{string.Join(" | ", row.Words.Select(w => w.Text))}]";
}
