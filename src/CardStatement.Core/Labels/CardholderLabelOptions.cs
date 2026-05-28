namespace CardStatement.Core.Labels;

public sealed class CardholderLabelOptions
{
    public Dictionary<string, Guid> Map { get; set; } = new(StringComparer.Ordinal);
}
