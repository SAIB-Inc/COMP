using Microsoft.EntityFrameworkCore;
using Comp.Sync.Data.Models;

namespace Comp.Sync.Data;

public class TokenMetadataDbContext(DbContextOptions<TokenMetadataDbContext> options) : DbContext(options)
{
    public DbSet<TokenMetadata> TokenMetadata => Set<TokenMetadata>();
    public DbSet<SyncState> SyncState => Set<SyncState>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TokenMetadata>().HasKey(tmd => tmd.Subject);
        modelBuilder.Entity<SyncState>().HasKey(ss => new { ss.Sha});
    }
}