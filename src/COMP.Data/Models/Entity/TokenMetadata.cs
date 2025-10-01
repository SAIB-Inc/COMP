namespace COMP.Data.Models.Entity;

public record TokenMetadata
{
    public string Subject { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Ticker { get; set; } = string.Empty;
    public string PolicyId { get; set; } = string.Empty;
    public int Decimals { get; set; }
    public string? Policy { get; set; }
    public string? Url { get; set; }
    public string? Logo { get; set; }
    public string? Description { get; set; }
}
