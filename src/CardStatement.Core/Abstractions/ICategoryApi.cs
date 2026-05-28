using CardStatement.Core.Models;

namespace CardStatement.Core.Abstractions;

public interface ICategoryApi
{
    Task<IReadOnlyList<Category>> GetAllAsync(CancellationToken ct = default);
}
