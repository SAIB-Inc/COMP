namespace Cardano.Metadata.Models;

public record SyncState
{
    public string Sha { get; set; } = string.Empty;
    public DateTime Date { get; set; }
}