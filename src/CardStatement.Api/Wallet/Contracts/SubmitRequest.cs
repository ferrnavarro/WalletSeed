namespace CardStatement.Api.Wallet.Contracts;

public sealed record SubmitRequest(string AccountId, IReadOnlyList<SubmitRequestRow> Rows);

public sealed record SubmitRequestRow(
    int Index,
    DateOnly Date,
    decimal SignedAmount,
    string Currency,
    string Description,
    string? CounterParty,
    string CardholderSectionRawName,
    string CategoryId);
