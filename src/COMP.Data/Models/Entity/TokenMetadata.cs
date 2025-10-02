namespace COMP.Data.Models.Entity;

public record TokenMetadata(
    string Subject,
    string Name,
    string Ticker,
    string PolicyId,
    int Decimals,
    string? Policy,
    string? Url,
    string? Logo,
    string? Description
);
