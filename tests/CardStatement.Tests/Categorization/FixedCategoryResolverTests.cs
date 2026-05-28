using CardStatement.Core.Categorization;
using CardStatement.Core.Models;
using FluentAssertions;

namespace CardStatement.Tests.Categorization;

public class FixedCategoryResolverTests
{
    private static readonly Category[] Taxonomy =
    [
        new(new("10c12b30-bf94-4b58-bac8-3e2e4528feb6"), "Debt"),
        new(new("3aea0a15-d1b5-46fd-9965-98868dd410ca"), "Loan, interests"),
        new(new("0ba25396-1fd3-477d-9b73-100a4229942b"), "Refunds (tax, purchase)"),
        new(new("40b565bb-d9cc-430a-a4ef-0c8649b636ab"), "Automatic bank statements reading"),
    ];

    [Fact]
    public void Resolves_names_to_taxonomy_guids()
    {
        var resolver = new FixedCategoryResolver(
            Taxonomy,
            new FixedCategoryNamesOptions(),
            new Guid("40b565bb-d9cc-430a-a4ef-0c8649b636ab"));

        resolver.Payment!.Name.Should().Be("Debt");
        resolver.FinancingCharge!.Name.Should().Be("Loan, interests");
        resolver.FinancingReversal!.Name.Should().Be("Refunds (tax, purchase)");
        resolver.Fallback!.Name.Should().Be("Automatic bank statements reading");
    }

    [Fact]
    public void Validation_warns_on_missing_fixed_name()
    {
        var names = new FixedCategoryNamesOptions { Payment = "NonExistent" };
        var resolver = new FixedCategoryResolver(Taxonomy, names, null);

        resolver.ValidateConfiguration(names).Should().Contain(w => w.Contains("Payment"));
    }
}
