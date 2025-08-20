namespace Comp.Models.Github;

public record GitTreeResponse
{
    public GitTreeItem[]? Tree { get; set; }
}
