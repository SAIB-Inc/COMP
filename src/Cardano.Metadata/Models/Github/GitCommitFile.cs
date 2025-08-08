namespace Cardano.Metadata.Models.Github;

public record GitCommitFile
{
    public string? Filename { get; set; }
    public string? RawUrl { get; set; }
}