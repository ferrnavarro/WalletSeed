namespace CardStatement.Api.Wallet.Contracts;

public sealed record CompareResponse(
    StatementWindowDto Window,
    WalletAccountDto Account,
    IReadOnlyList<WalletCategoryDto> Categories,
    IReadOnlyList<PdfRowDto> PdfRows,
    IReadOnlyList<WalletRowDto> WalletRows,
    IReadOnlyList<string> UnmappedSections);
