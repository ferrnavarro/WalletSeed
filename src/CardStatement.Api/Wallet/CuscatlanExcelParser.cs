using System.Globalization;
using ClosedXML.Excel;

namespace CardStatement.Api.Wallet;

public sealed record CuscatlanExcelTransaction(
    DateOnly Date,
    string Description,
    string? Reference,
    string CardLast4,
    decimal Amount);

public sealed record CuscatlanExcelParseResult(
    IReadOnlyList<CuscatlanExcelTransaction> Transactions);

public sealed class CuscatlanExcelParseException : Exception
{
    public CuscatlanExcelParseException(string message) : base(message)
    {
    }
}

public static class CuscatlanExcelParser
{
    private static readonly string[] DateFormats = { "yyyy/MM/dd", "dd/MM/yyyy" };

    internal sealed record ColumnMapping(int Card, int Date, int Reference, int Description, int Amount);

    public static CuscatlanExcelParseResult Parse(Stream stream)
    {
        var memory = ExcelStreamHelper.CopyToSeekable(stream);

        XLWorkbook workbook;
        try
        {
            workbook = new XLWorkbook(memory);
        }
        catch (Exception ex) when (ex is System.IO.FileFormatException or DocumentFormat.OpenXml.Packaging.OpenXmlPackageException or InvalidDataException)
        {
            throw new CuscatlanExcelParseException("The file could not be read. Please make sure it is a valid, unmodified bank export.");
        }

        using (workbook)
        {
            return ParseWorkbook(workbook);
        }
    }

    internal static CuscatlanExcelParseResult ParseWorkbook(XLWorkbook workbook)
    {
        var sheet = workbook.Worksheets.First();
        var (headerRow, columns) = FindHeader(sheet)
            ?? throw new CuscatlanExcelParseException("Could not find the transactions header row in the Excel file.");

        var transactions = new List<CuscatlanExcelTransaction>();
        var lastRow = sheet.LastRowUsed()!.RowNumber();
        for (var rowNumber = headerRow + 1; rowNumber <= lastRow; rowNumber++)
        {
            var tx = TryParseRow(sheet.Row(rowNumber), columns);
            if (tx is not null)
            {
                transactions.Add(tx);
            }
        }

        if (transactions.Count == 0)
        {
            throw new CuscatlanExcelParseException("No valid transactions found in the Excel file.");
        }

        return new CuscatlanExcelParseResult(transactions);
    }

    /// <summary>
    /// Returns the header row number and column mapping when the sheet looks like a Cuscatlán
    /// credit card export (header contains "Fecha", "Descripción" and "Dólares").
    /// Used by the bank auto-detector; null when the layout does not match.
    /// </summary>
    internal static (int HeaderRow, ColumnMapping Columns)? FindHeader(IXLWorksheet sheet)
    {
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 0;
        for (var rowNumber = 1; rowNumber <= Math.Min(lastRow, 20); rowNumber++)
        {
            var row = sheet.Row(rowNumber);
            var headers = Enumerable.Range(1, row.LastCellUsed()?.Address.ColumnNumber ?? 0)
                .Select(c => (Column: c, Text: row.Cell(c).GetString().Trim()))
                .Where(x => !string.IsNullOrWhiteSpace(x.Text))
                .ToList();

            var dateCol = headers.FirstOrDefault(h => string.Equals(h.Text, "Fecha", StringComparison.OrdinalIgnoreCase)).Column;
            var descCol = headers.FirstOrDefault(h => h.Text.StartsWith("Descripci", StringComparison.OrdinalIgnoreCase)).Column;
            var amountCol = headers.FirstOrDefault(h => string.Equals(h.Text, "Dólares", StringComparison.OrdinalIgnoreCase) || string.Equals(h.Text, "Dolares", StringComparison.OrdinalIgnoreCase)).Column;

            if (dateCol == 0 || descCol == 0 || amountCol == 0)
            {
                continue;
            }

            var cardCol = headers.FirstOrDefault(h => h.Text.StartsWith("Número de tarjeta", StringComparison.OrdinalIgnoreCase) || h.Text.StartsWith("Numero de tarjeta", StringComparison.OrdinalIgnoreCase)).Column;
            var refCol = headers.FirstOrDefault(h => string.Equals(h.Text, "Referencia", StringComparison.OrdinalIgnoreCase)).Column;

            return (rowNumber, new ColumnMapping(cardCol, dateCol, refCol, descCol, amountCol));
        }

        return null;
    }

    private static CuscatlanExcelTransaction? TryParseRow(IXLRow row, ColumnMapping columns)
    {
        var date = ParseDate(row.Cell(columns.Date).GetString());
        if (date is null)
        {
            return null;
        }

        var amount = ParseAmount(row.Cell(columns.Amount).GetString());
        if (amount == 0)
        {
            return null;
        }

        var reference = columns.Reference > 0 ? NullIfEmpty(row.Cell(columns.Reference).GetString()) : null;
        var rawDescription = NullIfEmpty(row.Cell(columns.Description).GetString());
        var description = rawDescription ?? (reference is not null ? $"Ref {reference}" : "Cuscatlán transaction");
        var cardLast4 = columns.Card > 0 ? ExtractCardLast4(row.Cell(columns.Card).GetString()) : "0000";

        return new CuscatlanExcelTransaction(date.Value, description, reference, cardLast4, amount);
    }

    private static DateOnly? ParseDate(string value)
    {
        var trimmed = value.Trim();
        if (DateOnly.TryParseExact(trimmed, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        return null;
    }

    private static decimal ParseAmount(string value)
    {
        // Formats seen: "$ 99.00", "- $ 343.12", "$ 1,234.56"
        var cleaned = value.Replace("$", string.Empty).Replace(",", string.Empty).Trim();
        var negative = cleaned.StartsWith('-');
        cleaned = cleaned.TrimStart('-').Trim();

        if (decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
        {
            return negative ? -amount : amount;
        }

        return 0m;
    }

    private static string ExtractCardLast4(string value)
    {
        var digits = value.Where(char.IsDigit).ToArray();
        return digits.Length >= 4 ? new string(digits[^4..]) : "0000";
    }

    private static string? NullIfEmpty(string value)
    {
        var trimmed = value.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}

internal static class ExcelStreamHelper
{
    /// <summary>
    /// Copies to a seekable MemoryStream: form-file streams may not support the
    /// seeking that the OpenXML reader requires.
    /// </summary>
    public static MemoryStream CopyToSeekable(Stream stream)
    {
        var memory = new MemoryStream();
        stream.CopyTo(memory);
        memory.Position = 0;
        return memory;
    }
}
