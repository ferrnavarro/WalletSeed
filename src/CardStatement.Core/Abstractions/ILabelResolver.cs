using CardStatement.Core.Models;

namespace CardStatement.Core.Abstractions;

public interface ILabelResolver
{
    Task<LabelResolution> ResolveAsync(string cardLast4, CancellationToken ct = default);
}

public sealed record LabelResolution(Guid? LabelId, string? LabelName, bool Unmapped);
