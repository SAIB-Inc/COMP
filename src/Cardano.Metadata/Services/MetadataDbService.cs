using Cardano.Metadata.Models.Entity;
using Cardano.Metadata.Models;
using Microsoft.EntityFrameworkCore;
using Cardano.Metadata.Models.Github;

namespace Cardano.Metadata.Services;

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
            string.IsNullOrEmpty(token.Ticker) ||
            token.Decimals < 0)
        {
            logger.LogWarning("Invalid token data. Name, Ticker, Subject or Decimals cannot be null or empty.");
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

        SyncState? existingSyncState = await dbContext.SyncState
            .FirstOrDefaultAsync(cancellationToken);

        if (existingSyncState is null)
        {
            var syncState = new SyncState
            {
                Hash = newSha,
                Date = newDate
            };
            await dbContext.SyncState.AddAsync(syncState, cancellationToken);
            logger.LogInformation("Sync state created with hash: {Hash}", newSha);
        }
        else
        {
            existingSyncState.Hash = newSha;
            existingSyncState.Date = newDate;
            dbContext.SyncState.Update(existingSyncState);
            logger.LogInformation("Sync state updated from {OldHash} to {NewHash}", existingSyncState.Hash, newSha);
        }
        await dbContext.SaveChangesAsync(cancellationToken);
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

        if (string.IsNullOrEmpty(updated.Name) ||
           string.IsNullOrEmpty(updated.Ticker) ||
           updated.Decimals < 0)
        {
            logger.LogWarning("Invalid token data. Name, Ticker, Subject or Decimals cannot be null or empty.");
            return null;
        }

        // Preserve immutable fields from existing if needed; here we accept the provided updated entity
        dbContext.TokenMetadata.Update(updated);
        await dbContext.SaveChangesAsync(cancellationToken);
        return updated;
    }
}
