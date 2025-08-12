namespace Cardano.Metadata.Models.Github;

public record GitTreeResponse
{
    public GitTreeItem[]? Tree { get; set; }
}