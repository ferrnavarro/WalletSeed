namespace CardStatement.Core.Models;

public sealed record Label(
    Guid Id,
    string Name,
    string? Color = null,
    bool Archived = false);
