using System.Globalization;
using ClosedXML.Excel;

namespace CardStatement.Api.Wallet;

public sealed record CuscatlanAccountExcelTransaction(
    DateOnly Date,
    string Description,
    string? Reference,
    string Currency,
    string AccountLast4,
    decimal SignedAmount);

public sealed record CuscatlanAccountExcelParseResult(
    IReadOnlyList<CuscatlanAccountExcelTransaction> Transactions);

public sealed class CuscatlanAccountExcelParseException : Exception
{
    public CuscatlanAccountExcelParseException(string message) : base(message)
    {
    }
}

/// <summary>
/// Parses Banco Cuscatlán bank-account ("Historial de movimientos") Excel exports.
/// Unlike the Cuscatlán credit-card export, these are checking-account movements:
/// the header is "Fecha | Detalles de movimientos | Monto | Saldo disponible | Referencia",
/// there is no card column, and the amount sign already follows the Wallet convention
/// (positive = money in, negative = money out).
/// </summary>
public static class CuscatlanAccountExcelParser
{
    private static readonly string[] DateFormats = { "dd/MM/yyyy", "d/M/yyyy", "yyyy/MM/dd" };

    internal sealed record ColumnMapping(int Date, int Description, int Amount, int Reference);

    public static CuscatlanAccountExcelParseResult Parse(Stream stream)
    {
        var memory = ExcelStreamHelper.CopyToSeekable(stream);

        XLWorkbook workbook;
        try
        {
            workbook = new XLWorkbook(memory);
        }
        catch (Exception ex) when (ex is System.IO.FileFormatException or DocumentFormat.OpenXml.Packaging.OpenXmlPackageException or InvalidDataException)
        {
            throw new CuscatlanAccountExcelParseException("The file could not be read. Please make sure it is a valid, unmodified bank export.");
        }

        using (workbook)
        {
            return ParseWorkbook(workbook);
        }
    }

    internal static CuscatlanAccountExcelParseResult ParseWorkbook(XLWorkbook workbook)
    {
        var sheet = workbook.Worksheets.First();
        var (headerRow, columns) = FindHeader(sheet)
            ?? throw new CuscatlanAccountExcelParseException("Could not find the transactions header row in the Excel file.");

        var accountLast4 = ExtractAccountLast4(sheet, headerRow);

        var transactions = new List<CuscatlanAccountExcelTransaction>();
        var lastRow = sheet.LastRowUsed()!.RowNumber();
        for (var rowNumber = headerRow + 1; rowNumber <= lastRow; rowNumber++)
        {
            var tx = TryParseRow(sheet.Row(rowNumber), columns, accountLast4);
            if (tx is not null)
            {
                transactions.Add(tx);
            }
        }

        if (transactions.Count == 0)
        {
            throw new CuscatlanAccountExcelParseException("No valid transactions found in the Excel file.");
        }

        return new CuscatlanAccountExcelParseResult(transactions);
    }

    /// <summary>
    /// Returns the header row number and column mapping when the sheet looks like a Cuscatlán
    /// bank-account export (header contains "Fecha", "Detalles de movimientos" and "Monto").
    /// "Saldo disponible" and "Referencia" are optional. Null when the layout does not match.
    /// </summary>
    internal static (int HeaderRow, ColumnMapping Columns)? FindHeader(IXLWorksheet sheet)
    {
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 0;
        for (var rowNumber = 1; rowNumber <= Math.Min(lastRow, 25); rowNumber++)
        {
            var row = sheet.Row(rowNumber);
            var headers = Enumerable.Range(1, row.LastCellUsed()?.Address.ColumnNumber ?? 0)
                .Select(c => (Column: c, Text: row.Cell(c).GetString().Trim()))
                .Where(x => !string.IsNullOrWhiteSpace(x.Text))
                .ToList();

            var dateCol = headers.FirstOrDefault(h => string.Equals(h.Text, "Fecha", StringComparison.OrdinalIgnoreCase)).Column;
            var descCol = headers.FirstOrDefault(h => h.Text.StartsWith("Detalles de movimiento", StringComparison.OrdinalIgnoreCase) || h.Text.StartsWith("Detalle", StringComparison.OrdinalIgnoreCase)).Column;
            var amountCol = headers.FirstOrDefault(h => string.Equals(h.Text, "Monto", StringComparison.OrdinalIgnoreCase)).Column;

            if (dateCol == 0 || descCol == 0 || amountCol == 0)
            {
                continue;
            }

            var refCol = headers.FirstOrDefault(h => string.Equals(h.Text, "Referencia", StringComparison.OrdinalIgnoreCase)).Column;

            return (rowNumber, new ColumnMapping(dateCol, descCol, amountCol, refCol));
        }

        return null;
    }

    /// <summary>
    /// Finds the "No. de cuenta:" metadata row above the header and keeps the last 4
    /// digits of the account number ("XXXXXXXXXXX-2037" → "2037"). Falls back to "0000".
    /// </summary>
    internal static string ExtractAccountLast4(IXLWorksheet sheet, int headerRow)
    {
        for (var rowNumber = 1; rowNumber < headerRow; rowNumber++)
        {
            var row = sheet.Row(rowNumber);
            var lastColumn = row.LastCellUsed()?.Address.ColumnNumber ?? 0;
            for (var column = 1; column <= lastColumn; column++)
            {
                var text = row.Cell(column).GetString().Trim();
                if (!text.StartsWith("No. de cuenta", StringComparison.OrdinalIgnoreCase) &&
                    !text.StartsWith("Numero de cuenta", StringComparison.OrdinalIgnoreCase) &&
                    !text.StartsWith("No. Cuenta", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var value = text[(text.IndexOf(':') + 1)..].Trim();
                if (string.IsNullOrWhiteSpace(value) && column < lastColumn)
                {
                    value = row.Cell(column + 1).GetString().Trim();
                }

                var digits = value.Where(char.IsDigit).ToArray();
                return digits.Length >= 4 ? new string(digits[^4..]) : "0000";
            }
        }

        return "0000";
    }

    private static CuscatlanAccountExcelTransaction? TryParseRow(IXLRow row, ColumnMapping columns, string accountLast4)
    {
        var date = ParseDate(row.Cell(columns.Date));
        if (date is null)
        {
            return null;
        }

        var (amount, currency) = ParseAmount(row.Cell(columns.Amount));
        if (amount == 0)
        {
            return null;
        }

        var reference = columns.Reference > 0 ? NullIfEmpty(row.Cell(columns.Reference).GetString()) : null;
        var rawDescription = NullIfEmpty(row.Cell(columns.Description).GetString());
        var description = rawDescription ?? (reference is not null ? $"Ref {reference}" : "Cuscatlán account movement");

        // Signs already follow the Wallet convention: positive = money in, negative = money out.
        return new CuscatlanAccountExcelTransaction(date.Value, description, reference, currency, accountLast4, amount);
    }

    private static DateOnly? ParseDate(IXLCell cell)
    {
        if (cell.DataType == XLDataType.DateTime)
        {
            return DateOnly.FromDateTime(cell.GetDateTime());
        }

        var trimmed = cell.GetString().Trim();
        if (DateOnly.TryParseExact(trimmed, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        return null;
    }

    /// <summary>
    /// Parses amount text such as "$ 700.00", "- $ 62.75" or "₡ 1 234,56" and infers the
    /// currency from the symbol (₡ → CRC, anything else → USD).
    /// </summary>
    private static (decimal Amount, string Currency) ParseAmount(IXLCell cell)
    {
        if (cell.DataType == XLDataType.Number)
        {
            return ((decimal)cell.GetDouble(), "USD");
        }

        var raw = cell.GetString().Trim();
        if (raw.Length == 0)
        {
            return (0m, "USD");
        }

        var currency = raw.Contains('₡') ? "CRC" : "USD";

        // Formats seen: "$ 700.00", "- $ 62.75"; also tolerate colones and thousands separators.
        var cleaned = raw
            .Replace("₡", string.Empty)
            .Replace("$", string.Empty)
            .Replace("\u00a0", string.Empty)
            .Replace(" ", string.Empty)
            .Trim();

        var negative = cleaned.StartsWith('-') || cleaned.StartsWith('(');
        cleaned = cleaned.TrimStart('-').TrimStart('(').TrimEnd(')').Trim();

        if (cleaned.Contains(',') && cleaned.Contains('.'))
        {
            // The last separator is the decimal one: "1.234,56" vs "1,234.56".
            cleaned = cleaned.LastIndexOf(',') > cleaned.LastIndexOf('.')
                ? cleaned.Replace(".", string.Empty).Replace(',', '.')
                : cleaned.Replace(",", string.Empty);
        }
        else if (IsDecimalComma(cleaned))
        {
            // Single comma followed by 1-2 digits: decimal separator.
            cleaned = cleaned.Replace(',', '.');
        }
        else
        {
            cleaned = cleaned.Replace(",", string.Empty);
        }

        if (decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
        {
            return (negative ? -amount : amount, currency);
        }

        return (0m, currency);
    }

    /// <summary>
    /// True when a single trailing comma separates 1-2 decimals ("12,50") rather than
    /// thousands ("1,234").
    /// </summary>
    private static bool IsDecimalComma(string value)
    {
        var first = value.IndexOf(',');
        return first >= 0
               && first == value.LastIndexOf(',')
               && first < value.Length - 1
               && value.Length - first - 1 <= 2;
    }

    private static string? NullIfEmpty(string value)
    {
        var trimmed = value.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}
