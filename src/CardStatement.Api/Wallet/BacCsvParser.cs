using System.Globalization;
using System.Text;

namespace CardStatement.Api.Wallet;

public sealed record BacCsvTransaction(
    DateOnly Date,
    string Description,
    decimal DollarsAmount);

public sealed record BacCsvParseResult(
    string CardLast4,
    IReadOnlyList<BacCsvTransaction> Transactions);

public sealed class BacCsvParseException : Exception
{
    public BacCsvParseException(string message) : base(message)
    {
    }
}

public static class BacCsvParser
{
    private static readonly string[] DateFormats = { "dd/MM/yyyy" };

    public static BacCsvParseResult Parse(Stream stream)
    {
        // BAC CSV exports are typically encoded in Latin-1 (Windows-1252), not UTF-8.
        // StreamReader will auto-detect UTF-8 BOM if present, otherwise use Latin-1.
        using var reader = new StreamReader(stream, Encoding.Latin1, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        
        string? line;
        var lineNumber = 0;
        string? cardLast4 = null;
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

            // Line 2 contains the card number in field 0 (e.g., "4593-78**-****-2127")
            if (lineNumber == 2)
            {
                cardLast4 = ExtractCardLast4(trimmed);
                continue;
            }

            // Skip header rows (lines 1, 3, 4, 5 and empty lines)
            if (lineNumber <= 5 || string.IsNullOrWhiteSpace(trimmed))
            {
                continue;
            }

            // Try to parse as a transaction row
            var tx = TryParseTransaction(trimmed);
            if (tx is not null)
            {
                transactions.Add(tx);
            }
        }

        if (string.IsNullOrWhiteSpace(cardLast4))
        {
            throw new BacCsvParseException("Could not find card number in the CSV header.");
        }

        if (transactions.Count == 0)
        {
            throw new BacCsvParseException("No valid transactions found in the CSV file.");
        }

        return new BacCsvParseResult(cardLast4, transactions);
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

    private static BacCsvTransaction? TryParseTransaction(string line)
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

        return new BacCsvTransaction(date, description, dollars);
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
