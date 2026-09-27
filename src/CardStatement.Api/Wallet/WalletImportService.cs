using CardStatement.Api.ErrorHandling;
using CardStatement.Api.Wallet.Contracts;
using CardStatement.Core.Abstractions;
using CardStatement.Core.Banks.Exceptions;
using CardStatement.Core.Models;
using Microsoft.Extensions.Options;

namespace CardStatement.Api.Wallet;

public sealed record FileImportError(string FileName, string Message);

public sealed record FileImportCompareResult(
    CompareResponse Response,
    IReadOnlyList<FileImportError> FileErrors);

public sealed class NoValidImportFilesException : Exception
{
    public NoValidImportFilesException(string message) : base(message) { }
}

public sealed class WalletImportService
{
    private readonly IPdfExtractor _pdfExtractor;
    private readonly IBankResolver _bankResolver;
    private readonly IReconciler _reconciler;
    private readonly IWalletApiClient _walletClient;
    private readonly LabelMappingResolver _labelMapping;
    private readonly WalletOptions _options;
    private readonly ILogger<WalletImportService> _logger;

    public WalletImportService(
        IPdfExtractor pdfExtractor,
        IBankResolver bankResolver,
        IReconciler reconciler,
        IWalletApiClient walletClient,
        LabelMappingResolver labelMapping,
        IOptions<WalletOptions> options,
        ILogger<WalletImportService> logger)
    {
        _pdfExtractor = pdfExtractor;
        _bankResolver = bankResolver;
        _reconciler = reconciler;
        _walletClient = walletClient;
        _labelMapping = labelMapping;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<CompareResponse> CompareAsync(string tempPdfPath, string accountId, CancellationToken ct = default)
    {
        var words = _pdfExtractor.Extract(tempPdfPath);
        if (words.Words == null || words.Words.Count == 0)
        {
            throw new NoTextExtractableException("No text words found in PDF");
        }

        var (bank, statement) = _bankResolver.Resolve(words);
        if (statement.Sections == null || statement.Sections.Count == 0 || !statement.Sections.SelectMany(s => s.Transactions).Any())
        {
            throw new UnrecognizedLayoutException("No cardholder sections or transactions found in parsed statement");
        }

        var reconciled = _reconciler.Reconcile(statement);
        var issueDate = reconciled.Period.IssueDate;
        var cutoffDate = reconciled.Period.CutoffDate;
        var transactions = reconciled.Sections.SelectMany(s => s.Transactions).ToList();
        var oldestTxDate = transactions.Count > 0 ? transactions.Min(tx => tx.TransactionDate) : issueDate;
        var newestTxDate = transactions.Count > 0 ? transactions.Max(tx => tx.TransactionDate) : cutoffDate;
        var window = new StatementWindowDto(oldestTxDate.AddDays(-5), newestTxDate.AddDays(5), issueDate, cutoffDate);

        var accountsTask = _walletClient.ListAccountsAsync(ct);
        var categoriesTask = _walletClient.ListCategoriesAsync(ct);
        var recordsTask = _walletClient.ListRecordsAsync(accountId, window.From, window.To, ct);
        var labelsTask = _walletClient.ListLabelsAsync(ct);

        await Task.WhenAll(accountsTask, categoriesTask, recordsTask, labelsTask);

        var accounts = (await accountsTask).Where(a => !a.Archived).ToList();
        var categories = (await categoriesTask).ToList();
        var records = (await recordsTask).ToList();
        var labels = (await labelsTask).ToList();

        var account = accounts.FirstOrDefault(a => a.Id == accountId) ?? accounts.FirstOrDefault() ?? new WalletAccount(accountId, "Unknown", "USD", "CreditCard", false);
        var pdfRows = reconciled.Sections.SelectMany(s => s.Transactions.Select(tx => new { tx, s }))
            .Select((item, index) => new PdfRowInternal(
                index,
                item.tx.TransactionDate,
                item.tx.Amount * (item.tx.Direction == Direction.Expense ? -1 : 1),
                account.CurrencyCode,
                item.tx.RawDescription,
                null,
                item.s.RawName,
                item.tx.CardLast4)).ToList();

        var matches = DuplicateMatcher.Match(pdfRows, records);
        var walletRows = records.OrderBy(r => r.RecordDate).ThenBy(r => r.Id, StringComparer.Ordinal).Select(r => new WalletRowDto(r.Id, r.RecordDate, r.SignedAmount, r.CurrencyCode, r.Note, r.CounterParty, r.CategoryName, Array.Empty<int>())).ToList();

        var labelLookup = labels.Where(l => !l.Archived).ToDictionary(l => l.Id, l => l.Name, StringComparer.OrdinalIgnoreCase);
        var pdfRowDtos = pdfRows.Select(row =>
        {
            var previewLabelIds = _labelMapping.Resolve(row.CardholderSectionRawName);
            var previewLabelNames = previewLabelIds.Select(id => labelLookup.TryGetValue(id, out var name) ? name : id).ToList();
            return new PdfRowDto(
                row.Index,
                row.Date,
                row.SignedAmount,
                row.Currency,
                row.Description,
                null,
                row.CardholderSectionRawName,
                row.CardLast4,
                matches[row.Index].ToList(),
                matches[row.Index].Count == 0 && !string.Equals(account.CurrencyCode, row.Currency, StringComparison.Ordinal),
                false,
                previewLabelIds,
                previewLabelNames);
        }).ToList();

        var walletRowDtos = records.OrderBy(r => r.RecordDate).ThenBy(r => r.Id, StringComparer.Ordinal).Select((r, idx) => new WalletRowDto(
            r.Id,
            r.RecordDate,
            r.SignedAmount,
            r.CurrencyCode,
            r.Note,
            r.CounterParty,
            r.CategoryName,
            pdfRowDtos.Where(pr => pr.MatchedWalletRecordIds.Contains(r.Id)).Select(pr => pr.Index).OrderBy(i => i).ToArray())).ToList();

        var unmapped = _labelMapping.FindUnmapped(pdfRows.Select(r => r.CardholderSectionRawName));

        return new CompareResponse(window, new WalletAccountDto(account.Id, account.Name, account.CurrencyCode, account.AccountType), categories.Select(c => new WalletCategoryDto(c.Id, c.Name, c.Color)).ToList(), pdfRowDtos, walletRowDtos, unmapped.ToList());
    }

    public Task<FileImportCompareResult> CompareCsvAsync(IReadOnlyList<(string FileName, Stream Stream)> files, string accountId, CancellationToken ct = default)
        => CompareFilesAsync(files, accountId, ParseCsvFile, ct);

    public Task<FileImportCompareResult> CompareExcelAsync(IReadOnlyList<(string FileName, Stream Stream)> files, string accountId, CancellationToken ct = default)
        => CompareFilesAsync(files, accountId, ParseExcelFile, ct);

    private delegate List<PdfRowInternal> FileParser(Stream stream, ref int index);

    private static List<PdfRowInternal> ParseCsvFile(Stream stream, ref int index)
    {
        var result = BacCsvParser.Parse(stream);
        var rows = new List<PdfRowInternal>();
        foreach (var tx in result.Transactions)
        {
            // CSV: positive Dollars = expense → Wallet: negative signedAmount
            rows.Add(new PdfRowInternal(
                index++,
                tx.Date,
                -tx.DollarsAmount,
                "USD",
                tx.Description,
                null,
                string.Empty,
                result.CardLast4));
        }

        return rows;
    }

    private static List<PdfRowInternal> ParseExcelFile(Stream stream, ref int index)
    {
        var result = PromericaExcelParser.Parse(stream);
        var rows = new List<PdfRowInternal>();
        foreach (var tx in result.Transactions)
        {
            // Promerica: Creditos = money in (income → positive), Debitos = money out (expense → negative)
            var signedAmount = tx.Creditos > 0 ? tx.Creditos : -tx.Debitos;
            rows.Add(new PdfRowInternal(
                index++,
                tx.Date,
                signedAmount,
                "USD",
                tx.Description,
                null,
                string.Empty,
                tx.CardLast4));
        }

        return rows;
    }

    private async Task<FileImportCompareResult> CompareFilesAsync(
        IReadOnlyList<(string FileName, Stream Stream)> files,
        string accountId,
        FileParser parseFile,
        CancellationToken ct)
    {
        var pdfRows = new List<PdfRowInternal>();
        var fileErrors = new List<FileImportError>();
        var index = 0;

        foreach (var (fileName, stream) in files)
        {
            try
            {
                pdfRows.AddRange(parseFile(stream, ref index));
            }
            catch (Exception ex) when (ex is BacCsvParseException or PromericaExcelParseException or InvalidDataException)
            {
                _logger.LogWarning(ex, "Failed to parse import file {FileName}", fileName);
                var message = ex is BacCsvParseException or PromericaExcelParseException
                    ? ex.Message
                    : "The file could not be read. Please make sure it is a valid, unmodified bank export.";
                fileErrors.Add(new FileImportError(fileName, message));
            }
        }

        if (pdfRows.Count == 0)
        {
            throw new NoValidImportFilesException("None of the uploaded files could be parsed.");
        }

        var minDate = pdfRows.Min(r => r.Date);
        var maxDate = pdfRows.Max(r => r.Date);
        var window = new StatementWindowDto(minDate.AddDays(-5), maxDate.AddDays(5), minDate, maxDate);

        var accountsTask = _walletClient.ListAccountsAsync(ct);
        var categoriesTask = _walletClient.ListCategoriesAsync(ct);
        var recordsTask = _walletClient.ListRecordsAsync(accountId, window.From, window.To, ct);

        await Task.WhenAll(accountsTask, categoriesTask, recordsTask);

        var accounts = (await accountsTask).Where(a => !a.Archived).ToList();
        var categories = (await categoriesTask).ToList();
        var records = (await recordsTask).ToList();

        var account = accounts.FirstOrDefault(a => a.Id == accountId) ?? accounts.FirstOrDefault() ?? new WalletAccount(accountId, "Unknown", "USD", "CreditCard", false);
        var matches = DuplicateMatcher.Match(pdfRows, records);

        var pdfRowDtos = pdfRows.Select(row => new PdfRowDto(
            row.Index,
            row.Date,
            row.SignedAmount,
            row.Currency,
            row.Description,
            null,
            row.CardholderSectionRawName,
            row.CardLast4,
            matches[row.Index].ToList(),
            matches[row.Index].Count == 0,
            !string.Equals(account.CurrencyCode, row.Currency, StringComparison.Ordinal),
            Array.Empty<string>(),
            Array.Empty<string>())).ToList();

        var walletRowDtos = records.OrderBy(r => r.RecordDate).ThenBy(r => r.Id, StringComparer.Ordinal).Select(r => new WalletRowDto(
            r.Id,
            r.RecordDate,
            r.SignedAmount,
            r.CurrencyCode,
            r.Note,
            r.CounterParty,
            r.CategoryName,
            pdfRowDtos.Where(pr => pr.MatchedWalletRecordIds.Contains(r.Id)).Select(pr => pr.Index).OrderBy(i => i).ToArray())).ToList();

        var response = new CompareResponse(
            window,
            new WalletAccountDto(account.Id, account.Name, account.CurrencyCode, account.AccountType),
            categories.Select(c => new WalletCategoryDto(c.Id, c.Name, c.Color)).ToList(),
            pdfRowDtos,
            walletRowDtos,
            Array.Empty<string>());

        return new FileImportCompareResult(response, fileErrors);
    }

    public async Task<SubmitResponse> SubmitAsync(SubmitRequest req, CancellationToken ct = default)
    {
        var outcomes = new List<SubmitOutcomeDto>();
        var pending = req.Rows
            .Select((row, index) => new { row, index })
            .ToList();

        foreach (var batch in pending.Chunk(50))
        {
            var createRequests = batch.Select(item => new WalletCreateRequest(
                req.AccountId,
                item.row.Date.ToDateTime(new TimeOnly(12, 0)),
                item.row.SignedAmount,
                item.row.Currency,
                "credit_card",
                item.row.CategoryId,
                _labelMapping.Resolve(item.row.CardholderSectionRawName),
                item.row.Description,
                item.row.CounterParty)).ToList();

            var results = await _walletClient.CreateRecordsAsync(createRequests, ct);
            outcomes.AddRange(results.Select((result, offset) =>
            {
                var originalItem = batch[offset];
                return new SubmitOutcomeDto(
                    originalItem.row.Index,
                    result.Success,
                    result.Success ? result.Id : null,
                    result.Success ? null : result.Error);
            }));
        }

        return new SubmitResponse(outcomes.OrderBy(o => o.Index).ToList());
    }
}
