using System.Text.Json.Serialization;

namespace CardStatement.Core.Apis;

internal sealed class LabelPageDto
{
    [JsonPropertyName("labels")]
    public LabelDto[] Labels { get; set; } = [];

    [JsonPropertyName("limit")]
    public int Limit { get; set; }

    [JsonPropertyName("offset")]
    public int Offset { get; set; }
}

internal sealed class LabelDto
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("color")]
    public string? Color { get; set; }

    [JsonPropertyName("archived")]
    public bool Archived { get; set; }
}
