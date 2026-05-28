using CardStatement.Core.Labels;
using CardStatement.Core.Models;
using FluentAssertions;

namespace CardStatement.Tests.Labels;

public class LabelResolverTests
{
    private static readonly Guid LabelTitular = new("936a90c7-01c4-4bf4-805a-59733a925547");
    private static readonly Guid LabelDavid = new("16aa3eb4-e545-47d2-a45a-135b3475ac81");
    private static readonly Guid LabelGhost = new("00000000-0000-0000-0000-000000009999");

    private static readonly Label[] AvailableLabels =
    [
        new(LabelTitular, "BAC Titular"),
        new(LabelDavid, "BAC adicional(David)"),
    ];

    [Fact]
    public async Task Mapped_card_returns_label_id_and_name()
    {
        var resolver = new LabelResolver(
            new Dictionary<string, Guid> { ["2706"] = LabelTitular },
            AvailableLabels);

        var result = await resolver.ResolveAsync("2706");

        result.LabelId.Should().Be(LabelTitular);
        result.LabelName.Should().Be("BAC Titular");
        result.Unmapped.Should().BeFalse();
    }

    [Fact]
    public async Task Unmapped_card_returns_null_label_and_flag()
    {
        var resolver = new LabelResolver(
            new Dictionary<string, Guid> { ["2706"] = LabelTitular },
            AvailableLabels);

        var result = await resolver.ResolveAsync("9999");

        result.LabelId.Should().BeNull();
        result.LabelName.Should().BeNull();
        result.Unmapped.Should().BeTrue();
    }

    [Fact]
    public void Validation_warns_on_missing_label_id()
    {
        var resolver = new LabelResolver(
            new Dictionary<string, Guid> { ["2706"] = LabelGhost },
            AvailableLabels);

        resolver.ValidateConfiguration().Should().ContainSingle()
            .Which.Should().Contain("not found");
    }

    [Fact]
    public void Validation_warns_on_archived_label()
    {
        var archived = new Label(LabelTitular, "BAC Titular", Archived: true);
        var resolver = new LabelResolver(
            new Dictionary<string, Guid> { ["2706"] = LabelTitular },
            new[] { archived });

        resolver.ValidateConfiguration().Should().ContainSingle()
            .Which.Should().Contain("archived");
    }
}
