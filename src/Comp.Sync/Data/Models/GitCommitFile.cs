using System.Text.Json.Serialization;

namespace Comp.Sync.Data.Models;
public record GitCommitFile
{
    public string? Filename { get; init; }

    [JsonPropertyName("raw_url")]
    public string? RawUrl { get; init; }
}