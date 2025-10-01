using COMP.Data.Models.Entity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Argus.Sync.Data;

namespace COMP.Data.Data;

public class MetadataDbContext(DbContextOptions<MetadataDbContext> options, IConfiguration configuration) : CardanoDbContext(options, configuration)
{
    public DbSet<TokenMetadata> TokenMetadata => Set<TokenMetadata>();
    public DbSet<SyncState> SyncState => Set<SyncState>();
    public DbSet<TokenMetadataOnChain> TokenMetadataOnChain => Set<TokenMetadataOnChain>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<TokenMetadata>().HasKey(tmd => tmd.Subject);
        modelBuilder.Entity<TokenMetadata>()
        .HasIndex(tmd => new { tmd.Name, tmd.Description, tmd.Ticker })
        .HasDatabaseName("IX_TokenMetadata_Name_Description_Ticker");

        modelBuilder.Entity<SyncState>().HasKey(ss => ss.Hash);

        modelBuilder.Entity<TokenMetadataOnChain>(entity =>
        {
            entity.HasKey(e => e.Subject);
            entity.HasIndex(e => e.PolicyId);
        });
    }
}
