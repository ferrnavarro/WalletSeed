using CardStatement.Core.Models;

namespace CardStatement.Core.Abstractions;

public interface IResultBuilder
{
    Task<StatementResult> BuildAsync(Statement statement, CancellationToken ct = default);
}
