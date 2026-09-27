using CardStatement.Api.Wallet;
using Microsoft.Extensions.Options;

namespace CardStatement.Tests.Wallet;

public class LabelMappingResolverTests
{
    [Fact]
    public void Resolve_UsesCaseInsensitiveLookupAndAccentedNames()
    {
        var resolver = new LabelMappingResolver(Options.Create(new WalletOptions
        {
            LabelMapping = new Dictionary<string, string>
            {
                ["Café"] = "lbl-cafe",
            }
        }));

        Assert.Equal(new[] { "lbl-cafe" }, resolver.Resolve("café"));
        Assert.Equal(Array.Empty<string>(), resolver.Resolve("unknown"));
    }

    [Fact]
    public void FindUnmapped_ReturnsDistinctSortedNames()
    {
        var resolver = new LabelMappingResolver(Options.Create(new WalletOptions
        {
            LabelMapping = new Dictionary<string, string>
            {
                ["MAIN"] = "lbl-main",
            }
        }));

        var unmapped = resolver.FindUnmapped(new[] { "MAIN", "Other", "other", "Third" });

        Assert.Equal(new[] { "Other", "Third" }, unmapped);
    }

    [Fact]
    public void EmptyMapping_ReturnsNoMatches()
    {
        var resolver = new LabelMappingResolver(Options.Create(new WalletOptions
        {
            LabelMapping = new Dictionary<string, string>()
        }));

        Assert.Empty(resolver.Resolve("MAIN"));
        Assert.Equal(new[] { "MAIN" }, resolver.FindUnmapped(new[] { "MAIN" }));
    }
}
