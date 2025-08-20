using System.Text.Json.Serialization;

namespace Cardano.Metadata.Models.Response;

public record MetadataField<T>
{
    [JsonPropertyName("sequenceNumber")] public int? SequenceNumber { get; set; }
    [JsonPropertyName("value")] public T? Value { get; set; }
}

public record MetadataResponse
{
    [JsonPropertyName("subject")] public string? Subject { get; set; }
    [JsonPropertyName("policy")] public string? Policy { get; set; }

    [JsonPropertyName("name")] public MetadataField<string>? Name { get; set; }
    [JsonPropertyName("ticker")] public MetadataField<string>? Ticker { get; set; }
    [JsonPropertyName("description")] public MetadataField<string>? Description { get; set; }
    [JsonPropertyName("url")] public MetadataField<string>? Url { get; set; }
    [JsonPropertyName("logo")] public MetadataField<string>? Logo { get; set; }
    [JsonPropertyName("decimals")] public MetadataField<int>? Decimals { get; set; }
}

