using Argus.Sync.Extensions;
using Argus.Sync.Data.Models;
using Microsoft.EntityFrameworkCore;
using COMP.Data.Data;
using COMP.Sync.Services;
using COMP.API.Modules.Handlers;
using COMP.Sync.Reducers;
using System.Net.Http.Headers;
using System.Reflection;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Add Argus.Sync for blockchain synchronization - this registers the DbContext
builder.Services.AddCardanoIndexer<MetadataDbContext>(builder.Configuration);

// Register reducers - the assembly scanning will find CIP25Reducer
builder.Services.AddReducers<MetadataDbContext, IReducerModel>(builder.Configuration);
builder.Services.AddSingleton<MetadataHandler>();
builder.Services.AddSingleton<MetadataDbService>();
builder.Services.AddSingleton<GithubService>();
builder.Services.AddHostedService<GithubReducer>();

builder.Services.AddHttpClient("GithubApi", client =>
{
    client.BaseAddress = new Uri("https://api.github.com/");
    string productName = builder.Configuration["Github:UserAgent:ProductName"] ?? "CardanoTokenMetadataService";
    string productVersion = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "Unknown Version";
    string productUrl = builder.Configuration["Github:UserAgent:ProductUrl"] ?? "(+https://github.com/SAIB-Inc/COMP)";
    
    ProductInfoHeaderValue productValue = new(productName, productVersion);
    ProductInfoHeaderValue commentValue = new(productUrl);
    client.DefaultRequestHeaders.UserAgent.Add(productValue);
    client.DefaultRequestHeaders.UserAgent.Add(commentValue);
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", builder.Configuration["GithubPAT"]);
});

builder.Services.AddHttpClient("GithubRaw", client =>
{
    client.BaseAddress = new Uri("https://raw.githubusercontent.com/");
});

WebApplication app = builder.Build();

// Ensure database is up-to-date on startup (following Poki.Sync pattern)
using (IServiceScope scope = app.Services.CreateScope())
{
    ILoggerFactory loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();
    ILogger logger = loggerFactory.CreateLogger("DbInitialization");
    try
    {
        IDbContextFactory<MetadataDbContext> factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<MetadataDbContext>>();
        await using MetadataDbContext db = await factory.CreateDbContextAsync();

        bool hasMigrations = db.Database.GetMigrations().Any();
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