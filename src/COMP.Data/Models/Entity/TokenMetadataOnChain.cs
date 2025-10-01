using Argus.Sync.Data.Models;

namespace COMP.Data.Models.Entity;

public enum TokenType
{
    CIP25,
    CIP68
}

public record TokenMetadataOnChain(
    string Subject,
    string PolicyId,
    string AssetName,
    string Name,
    string Logo,
    string Description,
    long Quantity,
    int Decimals,
    TokenType TokenType
) : IReducerModel;