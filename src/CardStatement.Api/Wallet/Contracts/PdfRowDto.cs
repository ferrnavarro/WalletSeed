namespace CardStatement.Api.Wallet.Contracts;

public sealed record PdfRowDto(
    int Index,
    DateOnly Date,
    decimal SignedAmount,
    string Currency,
    string Description,
    string? CounterParty,
    string CardholderSectionRawName,
    string CardLast4,
    IReadOnlyList<string> MatchedWalletRecordIds,
    bool DefaultSelected,
    bool CurrencyMismatch,
    IReadOnlyList<string> PreviewLabelIds,
    IReadOnlyList<string> PreviewLabelNames);
