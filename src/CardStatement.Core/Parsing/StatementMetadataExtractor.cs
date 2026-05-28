using System.Globalization;
using System.Text.RegularExpressions;
using CardStatement.Core.Models;

namespace CardStatement.Core.Parsing;

public sealed partial class StatementMetadataExtractor
{
    [GeneratedRegex(@"^\d{4}-\d{2}[A-Z0-9]{2}-[A-Z0-9]{4}-\d{4}$", RegexOptions.CultureInvariant)]
    private static partial Regex MaskedAccountRegex();

    [GeneratedRegex(@"^\d{2}/\d{2}/\d{2}$", RegexOptions.CultureInvariant)]
    private static partial Regex ShortDateRegex();

    public StatementMetadata Extract(IReadOnlyList<PdfWord> words)
    {
        var page1 = words.Where(w => w.PageNumber == 1).ToList();

        var masked = page1.FirstOrDefault(w => MaskedAccountRegex().IsMatch(w.Text))?.Text
            ?? throw new InvalidOperationException("Masked account not found on page 1.");

        var cardType = ExtractCardType(page1, masked);
        var period = ExtractPeriod(page1);
        return new StatementMetadata(cardType, masked, period);
    }

    private static string ExtractCardType(IReadOnlyList<PdfWord> page1, string maskedAccount)
    {
        var maskedWord = page1.First(w => w.Text == maskedAccount);
        var sameRow = page1
            .Where(w => Math.Abs(w.Y - maskedWord.Y) <= 1.5)
            .Where(w => w.X < maskedWord.X)
            .OrderBy(w => w.X)
            .Select(w => w.Text);
        var text = string.Join(" ", sameRow).Trim();
        return string.IsNullOrEmpty(text) ? "UNKNOWN" : text;
    }

    private static StatementPeriod ExtractPeriod(IReadOnlyList<PdfWord> page1)
    {
        var emission = FindDateAfterLabel(page1, ["FECHA", "DE", "EMISION"]);
        var cutoff = FindDateAfterLabel(page1, ["FECHA", "DE", "CORTE"]);
        return new StatementPeriod(emission, cutoff);
    }

    private static DateOnly FindDateAfterLabel(IReadOnlyList<PdfWord> words, string[] label)
    {
        foreach (var first in words.Where(w => w.Text == label[0]))
        {
            var rowWords = words
                .Where(w => Math.Abs(w.Y - first.Y) <= 1.5)
                .OrderBy(w => w.X)
                .ToList();

            var idx = rowWords.FindIndex(w => ReferenceEquals(w, first));
            if (idx < 0 || idx + label.Length - 1 >= rowWords.Count)
                continue;

            var labelMatches = true;
            for (var i = 0; i < label.Length; i++)
            {
                if (rowWords[idx + i].Text != label[i])
                {
                    labelMatches = false;
                    break;
                }
            }
            if (!labelMatches) continue;

            for (var j = idx + label.Length; j < rowWords.Count; j++)
            {
                if (ShortDateRegex().IsMatch(rowWords[j].Text))
                {
                    return ParseShortDate(rowWords[j].Text);
                }
            }
        }

        throw new InvalidOperationException($"Date for label '{string.Join(' ', label)}' not found.");
    }

    private static DateOnly ParseShortDate(string token)
    {
        var parts = token.Split('/');
        var day = int.Parse(parts[0], CultureInfo.InvariantCulture);
        var month = int.Parse(parts[1], CultureInfo.InvariantCulture);
        var yy = int.Parse(parts[2], CultureInfo.InvariantCulture);
        var year = yy + 2000;
        return new DateOnly(year, month, day);
    }
}

public sealed record StatementMetadata(string CardType, string MaskedAccount, StatementPeriod Period);
