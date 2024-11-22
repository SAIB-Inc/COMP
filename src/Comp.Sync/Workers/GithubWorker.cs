using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using Cardano.Metadata.Data;
using Cardano.Metadata.Models;
using Microsoft.EntityFrameworkCore;

namespace Cardano.Metadata.Workers;

public class GithubWorker(
    ILogger<GithubWorker> logger,
    IConfiguration config,
    IHttpClientFactory httpClientFactory,
    IDbContextFactory<TokenMetadataDbContext> dbContextFactory) : BackgroundService
{
    private readonly ILogger<GithubWorker> _logger = logger;
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private readonly IDbContextFactory<TokenMetadataDbContext> _dbContextFactory = dbContextFactory;
    private readonly string _registryOwner = config["RegistryOwner"] ??
        throw new ArgumentNullException("RegistryOwner", "Registry owner must be configured");
    private readonly string _registryRepo = config["RegistryRepo"] ??
        throw new ArgumentNullException("RegistryRepo", "Registry repository must be configured");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Syncing Mappings");

            await using TokenMetadataDbContext dbContext = await _dbContextFactory.CreateDbContextAsync(stoppingToken);
            using HttpClient hc = _httpClientFactory.CreateClient("Github");
            SyncState? syncState = await dbContext.SyncState.FirstOrDefaultAsync(cancellationToken: stoppingToken);

            if (syncState is null)
            {
                await SyncAllMappings(hc, dbContext, stoppingToken);
            }
            else
            {
                await SyncChangesSinceLastSync(hc, dbContext, syncState, stoppingToken);
            }

            await Task.Delay(1000 * 60, stoppingToken);
        }
    }

    private async Task SyncAllMappings(HttpClient hc, TokenMetadataDbContext dbContext, CancellationToken stoppingToken)
    {
        _logger.LogWarning("No Sync State Information, syncing all mappings...");

        IEnumerable<GitCommit>? latestCommits = await hc
            .GetFromJsonAsync<IEnumerable<GitCommit>>(
                $"https://api.github.com/repos/{_registryOwner}/{_registryRepo}/commits",
                stoppingToken
            );

        if (latestCommits?.Any() != true)
        {
            _logger.LogError("Repo: {repo} Owner: {owner} has no commits!", _registryOwner, _registryRepo);
            return;
        }

        GitCommit latestCommit = latestCommits.First();
        GitTreeResponse? treeResponse = await hc
            .GetFromJsonAsync<GitTreeResponse>(
                $"https://api.github.com/repos/{_registryOwner}/{_registryRepo}/git/trees/{latestCommit.Sha}?recursive=true",
                stoppingToken
        );

        if (treeResponse?.Tree == null)
        {
            _logger.LogError("Repo: {repo} Owner: {owner} has no mappings!", _registryOwner, _registryRepo);
            return;
        }

        foreach (GitTreeItem item in treeResponse.Tree)
        {
            if (item.Path?.StartsWith("mappings/") == true && item.Path.EndsWith(".json"))
            {
                await ProcessMappingFile(hc, dbContext, item.Path!, latestCommit.Sha!, stoppingToken);
            }
        }

        await UpdateSyncStateAsync(latestCommit.Sha!, latestCommit.Commit?.Author?.Date ?? DateTime.UtcNow, stoppingToken);
    }

    private async Task SyncChangesSinceLastSync(HttpClient hc, TokenMetadataDbContext dbContext, SyncState syncState, CancellationToken stoppingToken)
    {
        _logger.LogInformation("Repo: {repo} Owner: {owner} checking for changes...", _registryOwner, _registryRepo);

        int page = 1;
        IEnumerable<GitCommit>? commitPage;

        do
        {
            commitPage = await hc.GetFromJsonAsync<IEnumerable<GitCommit>>(
                $"https://api.github.com/repos/{_registryOwner}/{_registryRepo}/commits?since={syncState.Date.AddSeconds(1):yyyy-MM-dd'T'HH:mm:ssZ}&page={page}",
                cancellationToken: stoppingToken
            );

            if (commitPage?.Any() != true) continue;

            foreach (GitCommit commit in commitPage)
            {
                await ProcessCommitFiles(hc, dbContext, commit, stoppingToken);
                await UpdateSyncStateAsync(commit.Sha!, commit.Commit?.Author?.Date ?? DateTime.UtcNow, stoppingToken);
            }
            page++;
        } while (commitPage?.Any() == true);
    }

    private async Task ProcessCommitFiles(HttpClient hc, TokenMetadataDbContext dbContext, GitCommit commit, CancellationToken stoppingToken)
    {
        GitCommit? resolvedCommit = await hc.GetFromJsonAsync<GitCommit>(commit.Url, cancellationToken: stoppingToken);

        if (resolvedCommit?.Files == null) return;

        foreach (GitCommitFile file in resolvedCommit.Files)
        {
            if (file.Filename?.StartsWith("mappings/") == true && file.Filename.EndsWith(".json"))
            {
                await ProcessMappingFile(hc, dbContext, file.Filename, commit.Sha!, stoppingToken);
            }
        }
    }

    private async Task ProcessMappingFile(HttpClient hc, TokenMetadataDbContext dbContext, string path, string sha, CancellationToken stoppingToken)
    {
        if (string.IsNullOrEmpty(path))
        {
            _logger.LogError("Item path is null or empty");
            return;
        }

        string subject = path
            .Replace("mappings/", string.Empty)
            .Replace(".json", string.Empty);

        var existingEntry = await dbContext.TokenMetadata
            .FirstOrDefaultAsync(t => t.Subject == subject, stoppingToken);

        if (existingEntry != null)
        {
            _logger.LogInformation("Subject {subject} already exists in the database. Skipping...", subject);
            return;
        }

        JsonElement mappingJson = await hc.GetFromJsonAsync<JsonElement>(
            $"https://raw.githubusercontent.com/{_registryOwner}/{_registryRepo}/{sha}/{path}",
            cancellationToken: stoppingToken
        );

        await SaveTokenMetadataAsync(mappingJson, stoppingToken);
    }


    // TODO: Refactor this to be more efficient
    private async Task UpdateSyncStateAsync(string sha, DateTime date, CancellationToken stoppingToken)
    {
        await using TokenMetadataDbContext? dbContext = await _dbContextFactory.CreateDbContextAsync(stoppingToken);

        SyncState? syncState = await dbContext.SyncState.FirstOrDefaultAsync(cancellationToken: stoppingToken);

        if (syncState != null)
        {
            dbContext.SyncState.Remove(syncState);
            await dbContext.SaveChangesAsync(stoppingToken);
        }

        var newSyncState = new SyncState
        {
            Sha = sha,
            Date = date
        };
        dbContext.SyncState.Add(newSyncState);

        await dbContext.SaveChangesAsync(stoppingToken);
        await dbContext.DisposeAsync();
    }

    private async Task SaveTokenMetadataAsync(JsonElement mappingJson, CancellationToken stoppingToken)
    {
        string subject = mappingJson.GetProperty("subject").GetString()!;
        string name = mappingJson.GetProperty("name").GetProperty("value").GetString()!;
        string description = mappingJson.GetProperty("description").GetProperty("value").GetString()!;

        string url = mappingJson.TryGetProperty("url", out JsonElement urlElement) 
            ? urlElement.GetProperty("value").GetString() ?? string.Empty : string.Empty;
        string logo = mappingJson.TryGetProperty("logo", out JsonElement logoElement) 
            ? logoElement.GetProperty("value").GetString() ?? string.Empty : string.Empty;
        int decimals = mappingJson.TryGetProperty("decimals", out JsonElement decimalsElement) 
            ? decimalsElement.GetProperty("value").GetInt32() : 0;
        string ticker = mappingJson.TryGetProperty("ticker", out JsonElement tickerElement) 
            ? tickerElement.GetProperty("value").GetString() ?? string.Empty : string.Empty;
        string policy = mappingJson.TryGetProperty("policy", out JsonElement policyElement)
            ? policyElement.GetString() ?? string.Empty
            : string.Empty;

        await using TokenMetadataDbContext? dbContext = await _dbContextFactory.CreateDbContextAsync(stoppingToken);

        await dbContext.TokenMetadata.AddAsync(new TokenMetadata
        {
            Subject = subject,
            Name = name,
            Description = description,
            Url = url,
            Logo = logo,
            Decimals = decimals,
            Ticker = ticker,
            Policy = policy
        }, stoppingToken);

        await dbContext.SaveChangesAsync(stoppingToken);
        await dbContext.DisposeAsync();
        _logger.LogInformation("Saved metadata for subject {subject}", subject);
    }
}
