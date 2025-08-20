namespace Comp.Models.Github;

public record GitCommit
{
    public string? Sha { get; set; }
    public string? Url { get; set; }
    public GitCommitInfo? Commit { get; set; }
    public IEnumerable<GitCommitFile>? Files { get; set; }
}
