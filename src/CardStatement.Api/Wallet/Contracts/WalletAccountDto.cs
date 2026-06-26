namespace CardStatement.Api.Wallet.Contracts;

public sealed record WalletAccountDto(string Id, string Name, string CurrencyCode, string AccountType);
