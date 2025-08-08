namespace Cardano.Metadata.Models.Entity;

public record SyncState
{
    public string Hash { get; set; } = string.Empty;
    public DateTimeOffset Date { get; set; }
}