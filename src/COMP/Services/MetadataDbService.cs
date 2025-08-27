using Comp.Models.Entity;
using Comp.Models;
using Microsoft.EntityFrameworkCore;
using Comp.Models.Github;

namespace Comp.Services;

public class MetadataDbService
(
    ILogger<MetadataDbService> logger,
    IDbContextFactory<MetadataDbContext> _dbContextFactory)
{
    public async Task<TokenMetadata?> AddTokenAsync(TokenMetadata token, CancellationToken cancellationToken)
    {
        using MetadataDbContext dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        if (string.IsNullOrEmpty(token.Subject) ||
            string.IsNullOrEmpty(token.Name) ||
            string.IsNullOrEmpty(token.Description) ||
            token.Decimals < 0)
        {
            logger.LogWarning("Invalid token data. Subject, Name, Description required; Decimals must be non-negative if present.");
            return null;
        }

        await dbContext.TokenMetadata.AddAsync(token, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return token;
    }

    public async Task<SyncState?> GetSyncStateAsync(CancellationToken cancellationToken)
    {
        using MetadataDbContext dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.SyncState
            .OrderByDescending(ss => ss.Date)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task UpsertSyncStateAsync(GitCommit latestCommit, CancellationToken cancellationToken)
    {
        using MetadataDbContext dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        string newSha = latestCommit.Sha ?? string.Empty;
        DateTimeOffset newDate = latestCommit.Commit?.Author?.Date ?? DateTimeOffset.UtcNow;

        // Delete existing sync state if it exists
        SyncState? existingSyncState = await dbContext.SyncState
            .FirstOrDefaultAsync(cancellationToken);

        if (existingSyncState is not null)
        {
            string oldHash = existingSyncState.Hash;
            dbContext.SyncState.Remove(existingSyncState);
            logger.LogInformation("Removed old sync state with hash: {OldHash}", oldHash);
        }

        // Insert new sync state
        var syncState = new SyncState
        {
            Hash = newSha,
            Date = newDate
        };
        await dbContext.SyncState.AddAsync(syncState, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Sync state created with hash: {Hash}", newSha);
    }

    public async Task<bool> SubjectExistsAsync(string subject, CancellationToken cancellationToken)
    {
        using MetadataDbContext dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.TokenMetadata
            .AnyAsync(t => t.Subject == subject, cancellationToken);
    }
    public async Task DeleteTokenAsync(string subject, CancellationToken cancellationToken)
    {
        using MetadataDbContext dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        TokenMetadata? existingMetadata = await dbContext.TokenMetadata
            .Where(tm => tm.Subject == subject)
            .FirstOrDefaultAsync(cancellationToken);

        if (existingMetadata != null)
        {
            dbContext.TokenMetadata.Remove(existingMetadata);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }
    
    public async Task<TokenMetadata?> UpdateTokenAsync(TokenMetadata updated, CancellationToken cancellationToken)
    {
        using MetadataDbContext dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        TokenMetadata? existingMetadata = await dbContext.TokenMetadata
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Subject == updated.Subject, cancellationToken);

        if (existingMetadata is null)
        {
            logger.LogWarning("Token metadata not found for subject {Subject}", updated.Subject);
            return null;
        }

        if (string.IsNullOrEmpty(updated.Subject) ||
            string.IsNullOrEmpty(updated.Name) ||
            string.IsNullOrEmpty(updated.Description) ||
            updated.Decimals < 0)
        {
            logger.LogWarning("Invalid token data. Subject, Name, Description required; Decimals must be non-negative if present.");
            return null;
        }

        // Preserve immutable fields from existing if needed; here we accept the provided updated entity
        dbContext.TokenMetadata.Update(updated);
        await dbContext.SaveChangesAsync(cancellationToken);
        return updated;
    }
}
