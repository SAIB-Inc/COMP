using Argus.Sync.Extensions;
using Argus.Sync.Data.Models;
using Microsoft.EntityFrameworkCore;
using Comp.Models;

var builder = WebApplication.CreateBuilder(args);

// Add Argus.Sync for blockchain synchronization - this registers the DbContext
builder.Services.AddCardanoIndexer<MetadataDbContext>(builder.Configuration);

// Register reducers - the assembly scanning will find CIP25Reducer
builder.Services.AddReducers<MetadataDbContext, IReducerModel>(builder.Configuration);

var app = builder.Build();

// Ensure database is up-to-date on startup (following Poki.Sync pattern)
using (var scope = app.Services.CreateScope())
{
    var loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();
    var logger = loggerFactory.CreateLogger("DbInitialization");
    try
    {
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<MetadataDbContext>>();
        await using var db = await factory.CreateDbContextAsync();

        var hasMigrations = db.Database.GetMigrations().Any();
        if (hasMigrations)
        {
            logger.LogInformation("Applying database migrations...");
            await db.Database.MigrateAsync();
            logger.LogInformation("Database migrated successfully or already up-to-date.");
        }
        else
        {
            logger.LogInformation("No EF migrations found; ensuring database is created.");
            await db.Database.EnsureCreatedAsync();
            logger.LogInformation("Database created successfully.");
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Database initialization failed");
        throw;
    }
}

app.MapGet("/", () => "COMP.Sync - Cardano Metadata Indexer");

app.Run();