using System.Text.Json;
using Cardano.Metadata.Data;
using Cardano.Metadata.Models;
using Microsoft.EntityFrameworkCore;

namespace Cardano.Metadata.Workers;

public class TokenMetadataWorker(
    ILogger<TokenMetadataWorker> logger,
    IConfiguration config,
    IHttpClientFactory httpClientFactory,
    IDbContextFactory<TokenMetadataDbContext> dbContextFactory) : BackgroundService
{
    private readonly ILogger<TokenMetadataWorker> _logger = logger;
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
            SyncState? syncState = await dbContext.SyncState.FirstOrDefaultAsync(cancellationToken: stoppingToken);
            await dbContext.DisposeAsync();

            if (syncState is null)
            {
                await SyncAllMappingsAsync(stoppingToken);
            }
            else
            {
                await SyncSinceLastStateAsync(syncState, stoppingToken);
            }

            await Task.Delay(1000 * 60, stoppingToken);
        }
    }

    private async Task SyncAllMappingsAsync(CancellationToken stoppingToken)
    {
        _logger.LogWarning("No Sync State Information, syncing all mappings...");

        HttpClient? apiClient = _httpClientFactory.CreateClient("GithubApi");

        IEnumerable<GitCommit>? latestCommits = await apiClient
            .GetFromJsonAsync<IEnumerable<GitCommit>>(
                $"repos/{_registryOwner}/{_registryRepo}/commits", 
                stoppingToken
            );

        if (latestCommits?.Any() != true)
        {
            _logger.LogError("Repo: {repo} Owner: {owner} has no commits!", _registryOwner, _registryRepo);
            return;
        }

        GitCommit latestCommit = latestCommits.First();
        GitTreeResponse? treeResponse = await apiClient
            .GetFromJsonAsync<GitTreeResponse>(
                $"repos/{_registryOwner}/{_registryRepo}/git/trees/{latestCommit.Sha}?recursive=true",
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
                await ProcessMappingFileAsync(item.Path!, latestCommit.Sha!, stoppingToken);
            }
        }

        await UpdateSyncStateAsync(latestCommit.Sha!, latestCommit.Commit?.Author?.Date ?? DateTime.UtcNow, stoppingToken);
    }

    private async Task SyncSinceLastStateAsync(SyncState syncState, CancellationToken stoppingToken)
    {
        _logger.LogInformation("Repo: {repo} Owner: {owner} checking for changes...", _registryOwner, _registryRepo);

        int page = 1;
        IEnumerable<GitCommit>? commitPage;

        HttpClient? apiClient = _httpClientFactory.CreateClient("GithubApi");

        do
        {
            commitPage = await apiClient.GetFromJsonAsync<IEnumerable<GitCommit>>(
                $"repos/{_registryOwner}/{_registryRepo}/commits?since={syncState.Date.AddSeconds(1):yyyy-MM-dd'T'HH:mm:ssZ}&page={page}",
                cancellationToken: stoppingToken
            );

            if (commitPage?.Any() != true) continue;

            foreach (GitCommit commit in commitPage)
            {
                await ProcessCommitFilesAsync(commit, stoppingToken);
                await UpdateSyncStateAsync(commit.Sha!, commit.Commit?.Author?.Date ?? DateTime.UtcNow, stoppingToken);
            }
            page++;
        } while (commitPage?.Any() == true);
    }

    private async Task ProcessCommitFilesAsync(GitCommit commit, CancellationToken stoppingToken)
    {
        HttpClient? apiClient = _httpClientFactory.CreateClient("GithubApi");

        GitCommit? resolvedCommit = await apiClient.GetFromJsonAsync<GitCommit>(commit.Url, cancellationToken: stoppingToken);

        if (resolvedCommit?.Files == null) return;

        foreach (GitCommitFile file in resolvedCommit.Files)
        {
            if (file.Filename?.StartsWith("mappings/") == true && file.Filename.EndsWith(".json"))
            {
                await ProcessMappingFileAsync(file.Filename, commit.Sha!, stoppingToken);
            }
        }
    }

    private async Task ProcessMappingFileAsync(string path, string sha, CancellationToken stoppingToken)
    {
        if (string.IsNullOrEmpty(path))
        {
            _logger.LogError("Item path is null or empty");
            return;
        }

        string subject = path
            .Replace("mappings/", string.Empty)
            .Replace(".json", string.Empty);

        await using TokenMetadataDbContext dbContext = await _dbContextFactory.CreateDbContextAsync(stoppingToken);
        
        TokenMetadata? existingEntry = await dbContext.TokenMetadata
            .FirstOrDefaultAsync(t => t.Subject == subject, stoppingToken);
        
        await dbContext.DisposeAsync();

        if (existingEntry != null)
        {
            _logger.LogInformation("Subject {subject} already exists in the database. Skipping...", subject);
            return;
        }

        HttpClient? rawClient = _httpClientFactory.CreateClient("GithubRaw");

        JsonElement mappingJson = await rawClient.GetFromJsonAsync<JsonElement>(
            $"{_registryOwner}/{_registryRepo}/{sha}/{path}",
            cancellationToken: stoppingToken
        );

        await SaveTokenMetadataAsync(mappingJson, stoppingToken);
    }

    private async Task UpdateSyncStateAsync(string sha, DateTime date, CancellationToken stoppingToken)
    {
        await using TokenMetadataDbContext? dbContext = await _dbContextFactory.CreateDbContextAsync(stoppingToken);

        SyncState? syncState = await dbContext.SyncState.FirstOrDefaultAsync(cancellationToken: stoppingToken);

        if (syncState != null)
        {
            dbContext.SyncState.Remove(syncState);
            await dbContext.SaveChangesAsync(stoppingToken);
        }

        SyncState? newSyncState = new SyncState
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

        await using TokenMetadataDbContext? dbContext = await _dbContextFactory.CreateDbContextAsync(stoppingToken);

        await dbContext.TokenMetadata.AddAsync(new TokenMetadata
        {
            Subject = subject,
            Name = name,
            Description = description,
            Url = url,
            Logo = logo,
            Decimals = decimals,
            Ticker = ticker
        }, stoppingToken);

        await dbContext.SaveChangesAsync(stoppingToken);
        await dbContext.DisposeAsync();
        _logger.LogInformation("Saved metadata for subject {subject}", subject);
    }
}
