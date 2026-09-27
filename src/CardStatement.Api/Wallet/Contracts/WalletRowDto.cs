namespace CardStatement.Api.Wallet.Contracts;

public sealed record WalletRowDto(
    string Id,
    DateOnly Date,
    decimal SignedAmount,
    string Currency,
    string? Note,
    string? CounterParty,
    string? CategoryName,
    IReadOnlyList<int> ClaimedByPdfIndices);
