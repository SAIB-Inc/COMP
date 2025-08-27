namespace Comp.Models.Entity;

public record SyncState
{
    public int Id { get; set; } = 1;
    public string Hash { get; set; } = string.Empty;
    public DateTimeOffset Date { get; set; }
}
