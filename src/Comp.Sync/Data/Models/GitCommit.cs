namespace Cardano.Metadata.Models;

public record GitCommit
{
    public string? Sha { get; init; }
    public string? Url { get; init; }
    public GitCommitInfo? Commit { get; init; }
    public IEnumerable<GitCommitFile>? Files { get; init; }
}