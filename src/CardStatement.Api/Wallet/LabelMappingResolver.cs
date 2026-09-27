using Microsoft.Extensions.Options;

namespace CardStatement.Api.Wallet;

public sealed class LabelMappingResolver
{
    private readonly IReadOnlyDictionary<string, string> _mappings;

    public LabelMappingResolver(IOptions<WalletOptions> options)
    {
        _mappings = options.Value.LabelMapping
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
}
