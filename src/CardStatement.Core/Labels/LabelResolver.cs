using CardStatement.Core.Abstractions;
using CardStatement.Core.Models;

namespace CardStatement.Core.Labels;

public sealed class LabelResolver : ILabelResolver
{
    private readonly IReadOnlyDictionary<string, Guid> _cardMap;
    private readonly IReadOnlyDictionary<Guid, Label> _labelsById;

    public LabelResolver(
        IReadOnlyDictionary<string, Guid> cardMap,
        IEnumerable<Label> availableLabels)
    {
        _cardMap = cardMap;
        _labelsById = availableLabels.ToDictionary(l => l.Id);
    }

    public Task<LabelResolution> ResolveAsync(string cardLast4, CancellationToken ct = default)
    {
        if (!_cardMap.TryGetValue(cardLast4, out var labelId))
            return Task.FromResult(new LabelResolution(null, null, Unmapped: true));

        if (!_labelsById.TryGetValue(labelId, out var label))
            return Task.FromResult(new LabelResolution(labelId, null, Unmapped: false));

        return Task.FromResult(new LabelResolution(label.Id, label.Name, Unmapped: false));
    }

    public IReadOnlyList<string> ValidateConfiguration()
    {
        var warnings = new List<string>();
        foreach (var (last4, labelId) in _cardMap)
        {
            if (!_labelsById.TryGetValue(labelId, out var label))
            {
                warnings.Add($"CardholderLabels[{last4}] = {labelId}: label id not found in Labels API.");
                continue;
            }
            if (label.Archived)
                warnings.Add($"CardholderLabels[{last4}] = {labelId}: label '{label.Name}' is archived.");
        }
        return warnings;
    }
}
