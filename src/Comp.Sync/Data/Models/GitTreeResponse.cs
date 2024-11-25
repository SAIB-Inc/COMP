namespace Comp.Sync.Data.Models;
public record GitTreeResponse
{
    public GitTreeItem[]? Tree { get; init; }
}