using System.Text.Json;
using Comp.Sync.Data;
using Comp.Sync.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Comp.Sync.Workers;

public class TokenMetadataWorker(
    ILogger<TokenMetadataWorker> logger,
    IConfiguration config,
    IHttpClientFactory httpClientFactory,
    IDbContextFactory<TokenMetadataDbContext> dbContextFactory) : BackgroundService
{
    private readonly ILogger<TokenMetadataWorker> _logger = logger;
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private readonly IDbContextFactory<TokenMetadataDbContext> _dbContextFactory = dbContextFactory;
    private readonly string _registryOwner = config.GetValue<string>("RegistryOwner") ?? 
        throw new ArgumentNullException("RegistryOwner", "Registry owner must be configured");
    private readonly string _registryRepo = config.GetValue<string>("RegistryRepo") ?? 
        throw new ArgumentNullException("RegistryRepo", "Registry repository must be configured");
    private readonly int _syncDelaySeconds = config.GetValue("SyncDelaySeconds", 60); 
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Syncing Token Metadata");

            SyncState? syncState = await GetSyncStateAsync(stoppingToken);

            if (syncState is null)
            {
                await SyncAllTokensAsync(stoppingToken);
            }
            else
            {
                await SyncSinceLastStateAsync(stoppingToken);
            }

            await Task.Delay(_syncDelaySeconds * 1000, stoppingToken);
        }
    }

    // Syncs all token metadata from the beginning of the registry
    private async Task SyncAllTokensAsync(CancellationToken stoppingToken)
    {
        _logger.LogWarning("No Sync State Information, syncing all mappings...");

        GitCommit? latestCommit = await GetLatestCommitAsync(stoppingToken);
        if (latestCommit is null || latestCommit.Sha is null)
        {
            throw new InvalidOperationException("Failed to retrieve the latest commit.");
        }

        GitTreeResponse? treeResponse = await GetGitTreeResponseAsync(latestCommit.Sha, stoppingToken);
        if (treeResponse?.Tree is null)
        {
            throw new InvalidOperationException("Failed to retrieve the Git tree response.");
        }

        await foreach (GitTreeItem item in ExtractGitTreeMappingFilesAsync(treeResponse))
        {
            if (item.Path is null)
            {
                _logger.LogWarning("Tree item has no path - Item: {Item}", item);
                continue;
            }

            if (await TokenMetadataExistsAsync(item.Path, stoppingToken)) continue;

            JsonElement mappingJson = await ExtractMappingJsonAsync(item.Path, latestCommit.Sha, stoppingToken);

            await SaveTokenMetadataAsync(mappingJson, stoppingToken);
        }

        await UpdateSyncStateAsync(latestCommit.Sha, latestCommit.Commit?.Author?.Date ?? DateTime.UtcNow, stoppingToken);
    }

    // Syncs the token metadata since the last sync state
    private async Task SyncSinceLastStateAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Repo: {repo} Owner: {owner} checking for changes...", _registryOwner, _registryRepo);

        int page = 1;
        IEnumerable<GitCommit>? commitPage;

        do
        {
            commitPage = await FetchCommitPageAsync(page, stoppingToken);

            if (commitPage?.Any() is not true) continue;

            foreach (GitCommit commit in commitPage)
            {
                if (commit.Url is null) 
                    throw new InvalidOperationException($"Invalid commit data received: Missing Commit Url");

                if (commit.Sha is null) 
                    throw new InvalidOperationException($"Invalid commit data received: Missing Commit Sha");

                GitCommit? resolvedCommit = await GetCommitDetailsAsync(commit.Url, stoppingToken);
                
                if (resolvedCommit is null)
                {
                    _logger.LogWarning("Failed to retrieve commit : {Sha}, URL: {Url}", commit.Sha, commit.Url);
                    continue;
                }

                if (resolvedCommit.Files is null)
                {
                    _logger.LogWarning("Commit has no files : {Sha}, URL: {Url}", commit.Sha, commit.Url);
                    continue;
                }

                await foreach (GitCommitFile? file in ExtractGitCommitMappingFilesAsync(resolvedCommit))
                {
                    if (file.Filename is null)
                    {
                        _logger.LogWarning("File in commit has no filename - SHA: {Sha}, URL: {Url}", commit.Sha, commit.Url);
                        continue;
                    }

                    if (await TokenMetadataExistsAsync(file.Filename, stoppingToken)) continue;


                    JsonElement mappingJson = await ExtractMappingJsonAsync(file.Filename, commit.Sha, stoppingToken);

                    await SaveTokenMetadataAsync(mappingJson, stoppingToken);
                }
            }
            page++;
        } while (commitPage?.Any() is true);
    }

    // Updates the sync state in the database
    private async Task UpdateSyncStateAsync(string sha, DateTime date, CancellationToken stoppingToken)
    {
        await using TokenMetadataDbContext? dbContext = await _dbContextFactory.CreateDbContextAsync(stoppingToken);

        SyncState? syncState = await GetSyncStateAsync(stoppingToken);

        if (syncState is not null)
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

    // Saves token metadata to the database
    private async Task SaveTokenMetadataAsync(JsonElement mappingJson, CancellationToken stoppingToken)
    {
        if (!mappingJson.TryGetProperty("subject", out JsonElement subjectElement) ||
            !mappingJson.TryGetProperty("name", out JsonElement nameElement) ||
            !nameElement.TryGetProperty("value", out JsonElement nameValueElement) ||
            !mappingJson.TryGetProperty("description", out JsonElement descElement) ||
            !descElement.TryGetProperty("value", out JsonElement descValueElement))
        {
            _logger.LogWarning("Skipping token metadata - missing required properties");
            return;
        }

        string subject = subjectElement.GetString()!;
        string name = nameValueElement.GetString()!;
        string description = descValueElement.GetString()!;

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

    // Fetches a page of commits from the Github API
    private async Task<IEnumerable<GitCommit>?> FetchCommitPageAsync(int page, CancellationToken stoppingToken)
    {
        HttpClient apiClient = _httpClientFactory.CreateClient("GithubApi");

        SyncState? lastSyncState = await GetSyncStateAsync(stoppingToken);

        return await apiClient.GetFromJsonAsync<IEnumerable<GitCommit>>(
            $"repos/{_registryOwner}/{_registryRepo}/commits?since={lastSyncState!.Date.AddSeconds(1):yyyy-MM-dd'T'HH:mm:ssZ}&page={page}",
            cancellationToken: stoppingToken
        );
    }

    // Fetches the tree response for a given commit SHA
    private async Task<GitTreeResponse?> GetGitTreeResponseAsync(string sha, CancellationToken stoppingToken)
    {
        HttpClient apiClient = _httpClientFactory.CreateClient("GithubApi");

        return await apiClient.GetFromJsonAsync<GitTreeResponse>(
            $"repos/{_registryOwner}/{_registryRepo}/git/trees/{sha}?recursive=true", stoppingToken);
    }

    // Fetches the latest commit from the Github API
    private async Task<GitCommit?> GetLatestCommitAsync(CancellationToken stoppingToken)
    {
        HttpClient apiClient = _httpClientFactory.CreateClient("GithubApi");

        IEnumerable<GitCommit>? latestCommits = await apiClient
            .GetFromJsonAsync<IEnumerable<GitCommit>>($"repos/{_registryOwner}/{_registryRepo}/commits", stoppingToken);

        if (latestCommits?.Any() is not true)
        {
            return null;
        }

        return latestCommits.First();
    }

    // Fetches the details of a commit from the Github API with a given URL
    private async Task<GitCommit?> GetCommitDetailsAsync(
        string commitUrl,
        CancellationToken stoppingToken)
    {
        HttpClient apiClient = _httpClientFactory.CreateClient("GithubApi");

        return await apiClient.GetFromJsonAsync<GitCommit>(commitUrl, cancellationToken: stoppingToken);
    }

    // Checks if a token metadata entry already exists in the database
    private async Task<bool> TokenMetadataExistsAsync(string path, CancellationToken stoppingToken)
    {
        string subject = path
            .Replace("mappings/", string.Empty)
            .Replace(".json", string.Empty);

        await using TokenMetadataDbContext dbContext = await _dbContextFactory.CreateDbContextAsync(stoppingToken);

        TokenMetadata? existingEntry = await dbContext.TokenMetadata
            .FirstOrDefaultAsync(t => t.Subject == subject, stoppingToken);

        await dbContext.DisposeAsync();

        if (existingEntry is not null)
        {
            _logger.LogInformation("Subject {subject} already exists in the database. Skipping...", subject);
            return true;
        }

        return false;
    }

    // An async enumerable that filters out Git tree items that are in the mappings directory
    private async IAsyncEnumerable<GitTreeItem> ExtractGitTreeMappingFilesAsync(GitTreeResponse treeResponse)
    {
        if (treeResponse.Tree is null)
        {
            _logger.LogError("Tree response is null");
            yield break;
        }

        foreach (GitTreeItem item in treeResponse.Tree)
        {
            if (item.Path?.StartsWith("mappings/") is true && item.Path.EndsWith(".json"))
            {
                await Task.Yield();
                yield return item;
            }
        }
    }

    // An async enumerable that filters out Git commit files that are in the mappings directory
    private async IAsyncEnumerable<GitCommitFile> ExtractGitCommitMappingFilesAsync(GitCommit commit)
    {
        if (commit.Files is null)
        {
            _logger.LogError("Commit files are null");
            yield break;
        }

        foreach (GitCommitFile file in commit.Files)
        {
            if (file.Filename?.StartsWith("mappings/") == true && file.Filename.EndsWith(".json"))
            {
                await Task.Yield();
                yield return file;
            }
        }
    }

    // Retrieves the JSON content of a file from the Github API
    private async Task<JsonElement> ExtractMappingJsonAsync(string path, string sha, CancellationToken stoppingToken)
    {
        HttpClient? rawClient = _httpClientFactory.CreateClient("GithubRaw");

        JsonElement mappingJson = await rawClient.GetFromJsonAsync<JsonElement>(
            $"{_registryOwner}/{_registryRepo}/{sha}/{path}",
            cancellationToken: stoppingToken
        );

        return mappingJson;
    }

    // Retrieves the current sync state from the database
    private async Task<SyncState?> GetSyncStateAsync(CancellationToken stoppingToken)
    {
        await using TokenMetadataDbContext dbContext = await _dbContextFactory.CreateDbContextAsync(stoppingToken);
        SyncState? SyncState = await dbContext.SyncState.FirstOrDefaultAsync(cancellationToken: stoppingToken);
        await dbContext.DisposeAsync();
        return SyncState;
    }
}
