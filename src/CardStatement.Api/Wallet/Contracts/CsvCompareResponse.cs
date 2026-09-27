namespace CardStatement.Api.Wallet.Contracts;

public sealed record CsvCompareResponse(
    StatementWindowDto Window,
    WalletAccountDto Account,
    IReadOnlyList<WalletCategoryDto> Categories,
    IReadOnlyList<PdfRowDto> PdfRows,
    IReadOnlyList<WalletRowDto> WalletRows,
    IReadOnlyList<string> UnmappedSections,
    IReadOnlyList<CsvFileErrorDto> FileErrors);

public sealed record CsvFileErrorDto(string FileName, string Message);
