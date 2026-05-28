using System.Text.Json.Serialization;

namespace CardStatement.Core.Apis;

internal sealed class CategoryPageDto
{
    [JsonPropertyName("categories")]
    public CategoryDto[] Categories { get; set; } = [];

    [JsonPropertyName("limit")]
    public int Limit { get; set; }

    [JsonPropertyName("offset")]
    public int Offset { get; set; }

    [JsonPropertyName("nextOffset")]
    public int? NextOffset { get; set; }
}

internal sealed class CategoryDto
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("color")]
    public string? Color { get; set; }

    [JsonPropertyName("envelopeId")]
    public int? EnvelopeId { get; set; }

    [JsonPropertyName("cardinality")]
    public string? Cardinality { get; set; }
}
