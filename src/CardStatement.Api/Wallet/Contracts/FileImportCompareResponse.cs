namespace CardStatement.Api.Wallet.Contracts;

public sealed record FileImportCompareResponse(
    StatementWindowDto Window,
    WalletAccountDto Account,
    IReadOnlyList<WalletCategoryDto> Categories,
    IReadOnlyList<PdfRowDto> PdfRows,
    IReadOnlyList<WalletRowDto> WalletRows,
    IReadOnlyList<string> UnmappedSections,
    IReadOnlyList<FileImportErrorDto> FileErrors);

public sealed record FileImportErrorDto(string FileName, string Message);
