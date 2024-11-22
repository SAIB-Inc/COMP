namespace Cardano.Metadata.Models;

public record GitTreeResponse
{
    public GitTreeItem[]? Tree { get; init; }
}