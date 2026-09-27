namespace CardStatement.Api.Wallet;

public enum WalletApiErrorKind
{
    NotConfigured,
    CredentialsInvalid,
    Unavailable,
    Rejected
}

public sealed class WalletApiException : Exception
{
    public WalletApiErrorKind Kind { get; }
    public int? HttpStatus { get; }
    public string? BodyExcerpt { get; }

    public WalletApiException(WalletApiErrorKind kind, string message, int? httpStatus = null, string? bodyExcerpt = null)
        : base(message)
    {
        Kind = kind;
        HttpStatus = httpStatus;
        BodyExcerpt = bodyExcerpt;
    }
}
