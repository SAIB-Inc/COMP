namespace Comp.Sync.Data.Models.Dto;

public record SearchTokenMetadataDto
{
    public string? Name { get; init; }
    public string? Description { get; init; }
    public string? Ticker { get; init; }
    public int? Decimals { get; init; }
    public string? SortBy { get; init; }
    public int Offset { get; init; } = 0;
    public int Limit { get; init; } = 10;
    public bool SortDescending { get; init; } = false;
}