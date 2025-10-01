using Comp.Models;
using Comp.Models.Entity;
using Microsoft.EntityFrameworkCore;
using LinqKit;
using Microsoft.CodeAnalysis;

namespace Comp.Modules.Handlers;

public class MetadataHandler
(
    IDbContextFactory<MetadataDbContext> _dbContextFactory
)
{
    // Fetch data by subject (checks both registry and on-chain tables)
    public async Task<IResult> GetTokenMetadataAsync(string subject)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        // Query both tables sequentially
        var registryToken = await db.TokenMetadata
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Subject == subject);

        var onChainToken = await db.TokenMetadataOnChain
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Subject == subject);

        // If both are null, return 404
        if (registryToken is null && onChainToken is null)
            return Results.NotFound();

        // Prioritize on-chain data, fall back to registry
        var result = new
        {
            subject,
            policyId = onChainToken?.PolicyId ?? registryToken?.PolicyId ?? "",
            name = onChainToken?.Name ?? registryToken?.Name,
            ticker = registryToken?.Ticker,
            logo = onChainToken?.Logo ?? registryToken?.Logo,
            description = onChainToken?.Description ?? registryToken?.Description,
            decimals = onChainToken?.Decimals ?? registryToken?.Decimals ?? 0,
            quantity = onChainToken?.Quantity,
            assetName = onChainToken?.AssetName,
            tokenType = onChainToken?.TokenType.ToString(),
            policy = registryToken?.Policy,
            url = registryToken?.Url,
            hasOnChainData = onChainToken is not null,
            hasRegistryData = registryToken is not null
        };

        return Results.Ok(result);
    }

    // Fetch data by batch with additional filtering (checks both registry and on-chain tables)
    public async Task<IResult> BatchTokenMetadataAsync(
        List<string> subjects,
        int? limit,
        string? searchText,
        string? policyId,
        string? policy,
        int? offset,
        bool? includeEmptyName,
        bool? includeEmptyLogo,
        bool? includeEmptyTicker)
    {
        if (subjects == null || subjects.Count == 0)
            return Results.BadRequest("No subjects provided.");

        int effectiveOffset = offset ?? 0;
        bool requireName = !(includeEmptyName ?? false);
        bool requireLogo = !(includeEmptyLogo ?? false);
        bool requireTicker = !(includeEmptyTicker ?? false);

        List<string> distinctSubjects = [.. subjects.Distinct()];

        // Build predicate for registry metadata
        ExpressionStarter<TokenMetadata> registryPredicate = PredicateBuilder.New<TokenMetadata>(false);
        registryPredicate = registryPredicate.Or(token => distinctSubjects.Contains(token.Subject));

        if (!string.IsNullOrWhiteSpace(policyId))
        {
            registryPredicate = registryPredicate.And(token =>
                token.Subject.Substring(0, 56)
                    .Equals(policyId, StringComparison.OrdinalIgnoreCase));
        }
        if (requireName)
            registryPredicate = registryPredicate.And(token => !string.IsNullOrEmpty(token.Name));

        if (requireLogo)
            registryPredicate = registryPredicate.And(token => !string.IsNullOrEmpty(token.Logo));

        if (requireTicker)
            registryPredicate = registryPredicate.And(token => !string.IsNullOrEmpty(token.Ticker));

        if (!string.IsNullOrWhiteSpace(policy))
            registryPredicate = registryPredicate.And(token => token.Policy == policy);

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            registryPredicate = registryPredicate.And(token =>
                EF.Functions.ILike(token.Name, $"%{searchText}%") ||
                (token.Description != null && EF.Functions.ILike(token.Description, $"%{searchText}%")) ||
                EF.Functions.ILike(token.Ticker, $"%{searchText}%"));
        }

        // Build predicate for on-chain metadata
        ExpressionStarter<TokenMetadataOnChain> onChainPredicate = PredicateBuilder.New<TokenMetadataOnChain>(false);
        onChainPredicate = onChainPredicate.Or(token => distinctSubjects.Contains(token.Subject));

        if (!string.IsNullOrWhiteSpace(policyId))
        {
            onChainPredicate = onChainPredicate.And(token => token.PolicyId == policyId);
        }
        if (requireName)
            onChainPredicate = onChainPredicate.And(token => !string.IsNullOrEmpty(token.Name));

        if (requireLogo)
            onChainPredicate = onChainPredicate.And(token => !string.IsNullOrEmpty(token.Logo));

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            onChainPredicate = onChainPredicate.And(token =>
                EF.Functions.ILike(token.Name, $"%{searchText}%") ||
                (token.Description != null && EF.Functions.ILike(token.Description, $"%{searchText}%")));
        }

        await using var db = await _dbContextFactory.CreateDbContextAsync();

        // Query both tables sequentially
        var registryTokens = await db.TokenMetadata
            .AsNoTracking()
            .Where(registryPredicate)
            .ToListAsync();

        var onChainTokens = await db.TokenMetadataOnChain
            .AsNoTracking()
            .Where(onChainPredicate)
            .ToListAsync();

        // Merge results - prioritize on-chain, combine with registry
        var mergedResults = distinctSubjects
            .Select(subject =>
            {
                var registry = registryTokens.FirstOrDefault(t => t.Subject == subject);
                var onChain = onChainTokens.FirstOrDefault(t => t.Subject == subject);

                if (registry is null && onChain is null)
                    return null;

                return new
                {
                    subject = subject,
                    policyId = onChain?.PolicyId ?? registry?.PolicyId ?? "",
                    name = onChain?.Name ?? registry?.Name,
                    ticker = registry?.Ticker,
                    logo = onChain?.Logo ?? registry?.Logo,
                    description = onChain?.Description ?? registry?.Description,
                    decimals = onChain?.Decimals ?? registry?.Decimals ?? 0,
                    quantity = onChain?.Quantity,
                    assetName = onChain?.AssetName,
                    tokenType = onChain?.TokenType.ToString(),
                    policy = registry?.Policy,
                    url = registry?.Url,
                    hasOnChainData = onChain is not null,
                    hasRegistryData = registry is not null
                };
            })
            .Where(t => t is not null)
            .ToList();

        int total = mergedResults.Count;

        // Apply pagination
        if (limit.HasValue)
        {
            mergedResults = mergedResults.Skip(effectiveOffset).Take(limit.Value).ToList();
        }

        if (mergedResults.Count == 0)
            return Results.NotFound("No tokens found for the given subjects.");

        return Results.Ok(new { total, data = mergedResults });
    }
}
