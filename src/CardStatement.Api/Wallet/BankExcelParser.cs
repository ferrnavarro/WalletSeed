using ClosedXML.Excel;

namespace CardStatement.Api.Wallet;

public enum ExcelBankLayout
{
    Promerica,
    Cuscatlan,
}

public sealed record BankExcelTransaction(
    DateOnly Date,
    string Description,
    decimal SignedAmount,
    string CardLast4);

public sealed class UnrecognizedExcelLayoutException : Exception
{
    public UnrecognizedExcelLayoutException(string message) : base(message)
    {
    }
}

/// <summary>
/// Detects which bank layout an Excel statement uses and parses it into
/// Wallet-convention rows (expense = negative, income = positive).
/// </summary>
public static class BankExcelParser
{
    public static IReadOnlyList<BankExcelTransaction> Parse(Stream stream)
    {
        using var memory = ExcelStreamHelper.CopyToSeekable(stream);

        XLWorkbook workbook;
        try
        {
            workbook = new XLWorkbook(memory);
        }
        catch (Exception ex) when (ex is System.IO.FileFormatException or DocumentFormat.OpenXml.Packaging.OpenXmlPackageException or InvalidDataException)
        {
            throw new UnrecognizedExcelLayoutException("The file could not be read. Please make sure it is a valid, unmodified bank export.");
        }

        using (workbook)
        {
            var sheet = workbook.Worksheets.First();

            if (PromericaExcelParser.Matches(sheet))
            {
                var result = PromericaExcelParser.ParseWorkbook(workbook);
                // Promerica: Creditos = money in (positive), Debitos = money out (negative)
                return result.Transactions
                    .Select(tx => new BankExcelTransaction(
                        tx.Date,
                        tx.Description,
                        tx.Creditos > 0 ? tx.Creditos : -tx.Debitos,
                        tx.CardLast4))
                    .ToList();
            }

            if (CuscatlanExcelParser.FindHeader(sheet) is not null)
            {
                var result = CuscatlanExcelParser.ParseWorkbook(workbook);
                // Cuscatlán: positive Dólares = expense (negative), negative = payment/credit (positive)
                return result.Transactions
                    .Select(tx => new BankExcelTransaction(tx.Date, tx.Description, -tx.Amount, tx.CardLast4))
                    .ToList();
            }

            throw new UnrecognizedExcelLayoutException("Unrecognized Excel layout. Supported formats: Promerica and Banco Cuscatlán credit card exports.");
        }
    }
}
