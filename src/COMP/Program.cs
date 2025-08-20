using FastEndpoints;
using FastEndpoints.Swagger;
using Comp.Models;
using Microsoft.EntityFrameworkCore;
using Comp.Modules.Handlers;
using System.Net.Http.Headers;
using System.Reflection;
using Comp.Services;
using Comp.Workers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddFastEndpoints();
builder.Services.SwaggerDocument();

builder.Services.AddDbContextFactory<MetadataDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddSingleton<MetadataHandler>();
builder.Services.AddSingleton<MetadataDbService>();
builder.Services.AddSingleton<GithubService>();
builder.Services.AddHostedService<GithubWorker>();

builder.Services.AddHttpClient("GithubApi", client =>
{
    client.BaseAddress = new Uri("https://api.github.com/");
    var productName = builder.Configuration["Github:UserAgent:ProductName"] ?? "CardanoTokenMetadataService";
    var productVersion = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "Unknown Version";
    var productUrl = builder.Configuration["Github:UserAgent:ProductUrl"] ?? "(+https://github.com/SAIB-Inc/COMP)";
    
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

var app = builder.Build();

app.UseHttpsRedirection();

app.UseFastEndpoints();

if (app.Environment.IsDevelopment())
{
    app.UseSwaggerGen();
}

// Ensure database is up-to-date on startup (migrate if migrations exist; otherwise create schema)
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
            await db.Database.MigrateAsync();
            logger.LogInformation("Database migrated or already up-to-date.");
        }
        else
        {
            logger.LogInformation("No EF migrations found; ensuring database is created.");
            await db.Database.EnsureCreatedAsync();
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Database initialization failed");
        throw;
    }
}

app.Run();
