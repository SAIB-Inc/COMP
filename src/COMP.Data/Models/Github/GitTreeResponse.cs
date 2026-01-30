namespace COMP.Data.Models.Github;

public record GitTreeResponse
{
    public GitTreeItem[]? Tree { get; set; }
}
