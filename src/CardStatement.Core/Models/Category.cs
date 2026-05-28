namespace CardStatement.Core.Models;

public sealed record Category(
    Guid Id,
    string Name,
    string? Color = null,
    int? EnvelopeId = null,
    string? Cardinality = null);
