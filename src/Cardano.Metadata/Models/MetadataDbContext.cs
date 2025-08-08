using Cardano.Metadata.Models.Entity;
using Microsoft.EntityFrameworkCore;

namespace Cardano.Metadata.Models;

public class MetadataDbContext(DbContextOptions<MetadataDbContext> options) : DbContext(options)
{
    public DbSet<TokenMetadata> TokenMetadata => Set<TokenMetadata>();
    public DbSet<SyncState> SyncState => Set<SyncState>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TokenMetadata>().HasKey(tmd => tmd.Subject);
        modelBuilder.Entity<TokenMetadata>()
        .HasIndex(tmd => new { tmd.Name, tmd.Description, tmd.Ticker })
        .HasDatabaseName("IX_TokenMetadata_Name_Description_Ticker");

        modelBuilder.Entity<SyncState>().HasKey(ss => ss.Hash);
    }
}