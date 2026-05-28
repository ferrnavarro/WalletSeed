using CardStatement.Core.Models;

namespace CardStatement.Core.Abstractions;

public interface ILabelsApi
{
    Task<IReadOnlyList<Label>> GetAllAsync(CancellationToken ct = default);
}
