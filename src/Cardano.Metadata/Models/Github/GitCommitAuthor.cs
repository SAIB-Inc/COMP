namespace Cardano.Metadata.Models.Github;

public record GitCommitAuthor
{
    public string? Name { get; set; }
    public string? Email { get; set; }
    public DateTimeOffset? Date { get; set; }
}