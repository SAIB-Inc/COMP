namespace COMP.Data.Models.Request;

public record GetTokenMetadataRequest
{
    public string Subject { get; set; } = string.Empty;
}

public record BatchTokenMetadataRequest
{
    public List<string> Subjects { get; set; } = new();
    public int? Limit { get; set; }
    public string? SearchText { get; set; }
    public string? PolicyId { get; set; }
    public string? Policy { get; set; }
    public int? Offset { get; set; }
    public bool? IncludeEmptyName { get; set; }
    public bool? IncludeEmptyLogo { get; set; }
    public bool? IncludeEmptyTicker { get; set; }
}
