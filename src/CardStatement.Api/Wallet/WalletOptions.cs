namespace CardStatement.Api.Wallet;

public sealed class WalletOptions
{
    public string? BaseUrl { get; set; }
    public string? Jwt { get; set; }
    public int TimeoutSeconds { get; set; } = 30;
    public Dictionary<string, string> LabelMapping { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
