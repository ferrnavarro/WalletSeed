using System;
using System.Text.RegularExpressions;

namespace CardStatement.Core.Banks;

public sealed record BankInfo
{
    private static readonly Regex IdRegex = new(@"^[a-z0-9][a-z0-9-]{0,31}$", RegexOptions.Compiled);

    public string Id { get; }
    public string DisplayName { get; }

    public BankInfo(string id, string displayName)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Bank ID cannot be null or whitespace.", nameof(id));
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("Bank display name cannot be null or whitespace.", nameof(displayName));

        if (!IdRegex.IsMatch(id))
            throw new ArgumentException("Bank ID must match standard identifier pattern (lowercase, letters/digits/hyphens, up to 32 chars).", nameof(id));

        Id = id;
        DisplayName = displayName;
    }
}
