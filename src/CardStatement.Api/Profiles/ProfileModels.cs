namespace CardStatement.Api.Profiles;

public sealed class ProfileSettings
{
    public string? Name { get; set; }
    public string? WalletApiToken { get; set; }
    public Dictionary<string, string> LabelMapping { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed record ProfileDto(string Id, string Name);
