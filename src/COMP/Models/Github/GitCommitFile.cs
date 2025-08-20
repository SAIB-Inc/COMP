namespace Comp.Models.Github;

public record GitCommitFile
{
    public string? Filename { get; set; }
    public string? Status { get; set; }
    public string? RawUrl { get; set; }
}
