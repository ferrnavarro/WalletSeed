using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CardStatement.Api.Wallet;

public sealed record BacCsvTransaction(
    DateOnly Date,
    string Description,
    decimal DollarsAmount,
    string CardLast4);

public sealed record BacCsvParseResult(
    string CardLast4,
    IReadOnlyList<BacCsvTransaction> Transactions);

public sealed class BacCsvParseException : Exception
{
    public BacCsvParseException(string message) : base(message)
    {
    }
}

public static partial class BacCsvParser
{
    private static readonly string[] DateFormats = { "dd/MM/yyyy" };

    // Card number pattern like "4593-78**-****-2127" (groups of 4 separated by dashes, may contain *)
    [GeneratedRegex(@"^\d{4}-[\d*]{2,4}-?[\d*]*-?\d{4}$")]
    private static partial Regex CardNumberPattern();

    public static BacCsvParseResult Parse(Stream stream)
    {
        // BAC CSV exports are typically encoded in Latin-1 (Windows-1252), not UTF-8.
        // StreamReader will auto-detect UTF-8 BOM if present, otherwise use Latin-1.
        using var reader = new StreamReader(stream, Encoding.Latin1, detectEncodingFromByteOrderMarks: true, leaveOpen: true);

        string? line;
        var lineNumber = 0;
        string? headerCardLast4 = null;
        string? currentCardLast4 = null;
        var transactions = new List<BacCsvTransaction>();
        var inFooter = false;

        while ((line = reader.ReadLine()) is not null)
        {
            lineNumber++;
            var trimmed = line.Trim();

            // Stop parsing at the footer section
            if (trimmed.StartsWith("CURRENT Interest", StringComparison.OrdinalIgnoreCase))
            {
                inFooter = true;
                continue;
            }

            if (inFooter)
            {
                continue;
            }

            // Line 2 contains the primary card number in field 0 (e.g., "4593-78**-****-2127")
            if (lineNumber == 2)
            {
                headerCardLast4 = ExtractCardLast4(trimmed);
                currentCardLast4 = headerCardLast4;
                continue;
            }

            // Skip header rows (lines 1, 3, 4 and empty lines)
            if (lineNumber <= 4 || string.IsNullOrWhiteSpace(trimmed))
            {
                continue;
            }

            // Card section marker: col 0 empty, col 1 is a card number (e.g. ", 4593-78**-****-2533, 0.00, 0.00")
            var markerCard = TryParseCardMarker(trimmed);
            if (markerCard is not null)
            {
                currentCardLast4 = markerCard;
                continue;
            }

            // Try to parse as a transaction row
            var tx = TryParseTransaction(trimmed, currentCardLast4);
            if (tx is not null)
            {
                transactions.Add(tx);
            }
        }

        if (string.IsNullOrWhiteSpace(headerCardLast4))
        {
            throw new BacCsvParseException("Could not find card number in the CSV header.");
        }

        if (transactions.Count == 0)
        {
            throw new BacCsvParseException("No valid transactions found in the CSV file.");
        }

        return new BacCsvParseResult(headerCardLast4, transactions);
    }

    private static string? TryParseCardMarker(string line)
    {
        var fields = SplitCsvLine(line);
        if (fields.Length < 2)
        {
            return null;
        }

        // Marker rows have an empty first column and a card number in the second
        if (!string.IsNullOrWhiteSpace(fields[0]))
        {
            return null;
        }

        var candidate = fields[1].Trim();
        if (!CardNumberPattern().IsMatch(candidate))
        {
            return null;
        }

        var digits = candidate.Where(char.IsDigit).ToArray();
        return digits.Length >= 4 ? new string(digits[^4..]) : null;
    }

    private static string? ExtractCardLast4(string line)
    {
        var fields = SplitCsvLine(line);
        if (fields.Length == 0)
        {
            return null;
        }

        var cardNumber = fields[0].Trim();
        if (string.IsNullOrWhiteSpace(cardNumber))
        {
            return null;
        }

        // Extract last 4 digits from card number like "4593-78**-****-2127"
        var digits = cardNumber.Where(char.IsDigit).ToArray();
        if (digits.Length >= 4)
        {
            return new string(digits[^4..]);
        }

        return null;
    }

    private static BacCsvTransaction? TryParseTransaction(string line, string? cardLast4)
    {
        var fields = SplitCsvLine(line);
        if (fields.Length < 4)
        {
            return null;
        }

        // Column 0: Date (dd/MM/yyyy)
        var dateStr = fields[0].Trim();
        if (!DateOnly.TryParseExact(dateStr, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return null;
        }

        // Column 3: Dollars amount
        var dollarsStr = fields[3].Trim();
        if (!decimal.TryParse(dollarsStr, NumberStyles.Number, CultureInfo.InvariantCulture, out var dollars))
        {
            return null;
        }

        // Skip zero-amount rows
        if (dollars == 0)
        {
            return null;
        }

        // Column 1: Description
        var description = fields[1].Trim();
        if (string.IsNullOrWhiteSpace(description))
        {
            return null;
        }

        return new BacCsvTransaction(date, description, dollars, cardLast4 ?? "0000");
    }

    private static string[] SplitCsvLine(string line)
    {
        // Simple CSV split that handles quoted fields
        var fields = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    // Escaped quote
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        fields.Add(current.ToString());
        return fields.ToArray();
    }
}
