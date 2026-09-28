using Microsoft.Extensions.Options;
using CardStatement.Api.Profiles;

namespace CardStatement.Api.Wallet;

public sealed class LabelMappingResolver
{
    private readonly IReadOnlyDictionary<string, string> _mappings;

    public LabelMappingResolver(IOptions<WalletOptions> options)
        : this(options, null)
    {
    }

    [Microsoft.Extensions.DependencyInjection.ActivatorUtilitiesConstructor]
    public LabelMappingResolver(IOptions<WalletOptions> options, IProfileContext? profileContext)
    {
        var rawMappings = profileContext?.GetActiveLabelMapping() ?? options.Value.LabelMapping;
        _mappings = rawMappings
            .Where(kvp => !string.IsNullOrWhiteSpace(kvp.Key) && !string.IsNullOrWhiteSpace(kvp.Value))
            .GroupBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<string> Resolve(string cardholderRawName)
    {
        if (string.IsNullOrWhiteSpace(cardholderRawName))
        {
            return Array.Empty<string>();
        }

        return _mappings.TryGetValue(cardholderRawName, out var labelId) ? new[] { labelId } : Array.Empty<string>();
    }

    /// <summary>
    /// Resolves a label by card last-4. Only mapping keys that are exactly 4 digits
    /// are considered card keys, so name-based mappings (PDF flow) are unaffected.
    /// </summary>
    public IReadOnlyList<string> ResolveByCardLast4(string cardLast4)
    {
        if (string.IsNullOrWhiteSpace(cardLast4) || cardLast4.Length != 4 || !cardLast4.All(char.IsDigit))
        {
            return Array.Empty<string>();
        }

        return _mappings.TryGetValue(cardLast4, out var labelId) ? new[] { labelId } : Array.Empty<string>();
    }

    public IReadOnlyList<string> FindUnmapped(IEnumerable<string> cardholderRawNames)
    {
        return cardholderRawNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Where(name => !_mappings.ContainsKey(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Returns distinct card last-4s that have no card-based label mapping.
    /// </summary>
    public IReadOnlyList<string> FindUnmappedCards(IEnumerable<string> cardLast4s)
    {
        return cardLast4s
            .Where(last4 => !string.IsNullOrWhiteSpace(last4))
            .Select(last4 => last4.Trim())
            .Where(last4 => last4.Length == 4 && last4.All(char.IsDigit))
            .Where(last4 => !_mappings.ContainsKey(last4))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(last4 => last4, StringComparer.Ordinal)
            .ToList();
    }
}
