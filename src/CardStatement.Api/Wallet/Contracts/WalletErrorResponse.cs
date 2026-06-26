using CardStatement.Api.Contracts;

namespace CardStatement.Api.Wallet.Contracts;

public static class WalletErrorCodes
{
    public const string NotConfigured = "WALLET_NOT_CONFIGURED";
    public const string CredentialsInvalid = "WALLET_CREDENTIALS_INVALID";
    public const string Unavailable = "WALLET_UNAVAILABLE";
    public const string Rejected = "WALLET_REJECTED";
}

public sealed record WalletErrorResponse(ErrorBody Error);
