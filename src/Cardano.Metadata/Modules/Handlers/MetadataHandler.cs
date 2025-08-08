using Cardano.Metadata.Models;
using Cardano.Metadata.Models.Entity;
using Microsoft.EntityFrameworkCore;
using LinqKit;
using Microsoft.CodeAnalysis;

namespace Cardano.Metadata.Modules.Handlers;

public class MetadataHandler
(
    IDbContextFactory<MetadataDbContext> _dbContextFactory
)
{
    // Fetch data by subject
    public async Task<IResult> GetTokenMetadataAsync(string subject)
    {
        using MetadataDbContext db = await _dbContextFactory.CreateDbContextAsync();
        TokenMetadata? token = await db.TokenMetadata
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Subject == subject);

        if (token is null)
            return Results.NotFound();

        return Results.Ok(token);
    }

    // Fetch data by batch with additional filtering
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

        using MetadataDbContext db = await _dbContextFactory.CreateDbContextAsync();
        List<string> distinctSubjects = [.. subjects.Distinct()];

        ExpressionStarter<TokenMetadata> predicate = PredicateBuilder.New<TokenMetadata>(false);

        predicate = predicate.Or(token => distinctSubjects.Contains(token.Subject));

        if (!string.IsNullOrWhiteSpace(policyId))
        {
            predicate = predicate.And(token =>
                token.Subject.Substring(0, 56)
                    .Equals(policyId, StringComparison.OrdinalIgnoreCase));
        }
        if (requireName)
            predicate = predicate.And(token => !string.IsNullOrEmpty(token.Name));

        if (requireLogo)
            predicate = predicate.And(token => !string.IsNullOrEmpty(token.Logo));

        if (requireTicker)
            predicate = predicate.And(token => !string.IsNullOrEmpty(token.Ticker));

        if (!string.IsNullOrWhiteSpace(policy))
            predicate = predicate.And(token => token.Policy == policy);

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            predicate = predicate.And(token =>
                EF.Functions.ILike(token.Name, $"%{searchText}%") ||
                (token.Description != null && EF.Functions.ILike(token.Description, $"%{searchText}%")) ||
                EF.Functions.ILike(token.Ticker, $"%{searchText}%"));
        }
        IQueryable<TokenMetadata> query = db.TokenMetadata
            .AsNoTracking()
            .Where(predicate);

        int total = await query.CountAsync();

        if (limit.HasValue)
        {
            query = query.Skip(effectiveOffset).Take(limit.Value);
        }

        List<TokenMetadata> tokenList = await query.ToListAsync();

        if (tokenList.Count == 0)
            return Results.NotFound("No tokens found for the given subjects.");

        return Results.Ok(new { total, data = tokenList });
    }
}
