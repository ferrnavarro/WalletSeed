namespace CardStatement.Api.Wallet;

public sealed record WalletAccount(string Id, string Name, string CurrencyCode, string AccountType, bool Archived);

public sealed record WalletCategory(string Id, string Name, string? Color);

public sealed record WalletLabel(string Id, string Name, string? Color, bool Archived);

public sealed record WalletRecord(string Id, DateOnly RecordDate, decimal SignedAmount, string CurrencyCode, string? Note, string? CounterParty, string? CategoryName);

public sealed record WalletCreateRequest(
    string AccountId,
    DateTimeOffset RecordDate,
    decimal SignedAmount,
    string? CurrencyCode,
    string PaymentType,
    string CategoryId,
    IReadOnlyList<string> LabelIds,
    string? Note,
    string? CounterParty);

public sealed record WalletCreateOutcome(int InputIndex, bool Success, string? Id, string? Error);
