using System.Globalization;
using ClosedXML.Excel;

namespace CardStatement.Api.Wallet;

public sealed record PromericaExcelTransaction(
    DateOnly Date,
    string Description,
    string? Reference,
    string CardLast4,
    decimal Creditos,
    decimal Debitos);

public sealed record PromericaExcelParseResult(
    IReadOnlyList<PromericaExcelTransaction> Transactions);

public sealed class PromericaExcelParseException : Exception
{
    public PromericaExcelParseException(string message) : base(message)
    {
    }
}

public static class PromericaExcelParser
{
    private const string HeaderMarker = "Fecha Movimiento";
    private static readonly string[] DateTimeFormats = { "dd/MM/yyyy HH:mm:ss", "dd/MM/yyyy H:mm:ss", "dd/MM/yyyy" };

    public static PromericaExcelParseResult Parse(Stream stream)
    {
        using var memory = ExcelStreamHelper.CopyToSeekable(stream);

        XLWorkbook workbook;
        try
        {
            workbook = new XLWorkbook(memory);
        }
        catch (Exception ex) when (ex is System.IO.FileFormatException or DocumentFormat.OpenXml.Packaging.OpenXmlPackageException or InvalidDataException)
        {
            throw new PromericaExcelParseException("The file could not be read. Please make sure it is a valid, unmodified bank export.");
        }

        using (workbook)
        {
            return ParseWorkbook(workbook);
        }
    }

    internal static PromericaExcelParseResult ParseWorkbook(XLWorkbook workbook)
    {
        var sheet = workbook.Worksheets.First();

        var headerRow = FindHeaderRow(sheet);
        if (headerRow is null)
        {
            throw new PromericaExcelParseException("Could not find the transactions header row in the Excel file.");
        }

        var transactions = new List<PromericaExcelTransaction>();
        foreach (var row in sheet.Rows(headerRow.RowNumber() + 1, sheet.LastRowUsed()!.RowNumber()))
        {
            var tx = TryParseRow(row);
            if (tx is not null)
            {
                transactions.Add(tx);
            }
        }

        if (transactions.Count == 0)
        {
            throw new PromericaExcelParseException("No valid transactions found in the Excel file.");
        }

        return new PromericaExcelParseResult(transactions);
    }

    /// <summary>
    /// True when the sheet's header row matches the Promerica layout ("Fecha Movimiento" in column A).
    /// Used by the bank auto-detector.
    /// </summary>
    internal static bool Matches(IXLWorksheet sheet) => FindHeaderRow(sheet) is not null;

    private static IXLRow? FindHeaderRow(IXLWorksheet sheet)
    {
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 0;
        for (var rowNumber = 1; rowNumber <= Math.Min(lastRow, 20); rowNumber++)
        {
            var row = sheet.Row(rowNumber);
            if (string.Equals(row.Cell(1).GetString().Trim(), HeaderMarker, StringComparison.OrdinalIgnoreCase))
            {
                return row;
            }
        }

        return null;
    }

    private static PromericaExcelTransaction? TryParseRow(IXLRow row)
    {
        // Column C: Fecha operacion (preferred); fallback to column A: Fecha Movimiento
        var date = ParseDate(row.Cell(3).GetString()) ?? ParseDate(row.Cell(1).GetString());
        if (date is null)
        {
            return null;
        }

        var creditos = ParseAmount(row.Cell(6).GetString());
        var debitos = ParseAmount(row.Cell(7).GetString());
        if (creditos == 0 && debitos == 0)
        {
            return null;
        }

        var reference = NullIfEmpty(row.Cell(2).GetString());
        var rawDescription = NullIfEmpty(row.Cell(5).GetString());
        var description = rawDescription ?? (reference is not null ? $"Ref {reference}" : "Promerica transaction");
        var cardLast4 = ExtractCardLast4(row.Cell(4).GetString());

        return new PromericaExcelTransaction(date.Value, description, reference, cardLast4, creditos, debitos);
    }

    private static DateOnly? ParseDate(string value)
    {
        var trimmed = value.Trim();
        if (DateTime.TryParseExact(trimmed, DateTimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateTime))
        {
            return DateOnly.FromDateTime(dateTime);
        }

        return null;
    }

    private static decimal ParseAmount(string value)
    {
        var cleaned = value.Replace("$", string.Empty).Replace(",", string.Empty).Trim();
        if (decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
        {
            return amount;
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
