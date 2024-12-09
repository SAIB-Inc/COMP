namespace Comp.Sync.Data.Models;

public record GitCommitAuthor
{
    public string? Name { get; init; } 
    public string? Email { get; init; } 
    public DateTime? Date { get; init; } 
}