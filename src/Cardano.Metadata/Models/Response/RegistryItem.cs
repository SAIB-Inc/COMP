namespace Cardano.Metadata.Models.Response;

public record RegistryItem
{
    public string? Subject { get; set; }
    public string? Policy { get; set; }
    public int Decimals { get; set; }
    public string? Description { get; set; }
    public string? Name { get; set; }
    public string? Ticker { get; set; }
    public string? Url { get; set; }
    public string? Logo { get; set; }
}