using System.Text.Json;
using Cardano.Metadata.Models.Entity;
using Cardano.Metadata.Models.Github;
using Cardano.Metadata.Models.Response;
using Cardano.Metadata.Services;

namespace Cardano.Metadata.Workers;
public class GithubWorker
(
    ILogger<GithubWorker> logger,
    GithubService githubService,
    MetadataDbService metadataDbService) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                logger.LogInformation("Starting metadata synchronization cycle");

                SyncState? syncState = await metadataDbService.GetSyncStateAsync(stoppingToken);

                if (syncState is null)
                {
                    logger.LogWarning("No sync state found. Performing initial full synchronization...");
                    GitCommit? latestCommit = await githubService.GetCommitsAsync(stoppingToken);

                    if (latestCommit == null || string.IsNullOrEmpty(latestCommit.Sha))
                    {
                        logger.LogError("Commit SHA is null or empty for the latest commit.");
                        break;
                    }
                    GitTreeResponse? treeResponse = await githubService.GetGitTreeAsync(latestCommit.Sha, stoppingToken);

                    if (treeResponse == null || treeResponse.Tree == null)
                    {
                        logger.LogError("Tree response is null");
                        break;
                    }
                    foreach (GitTreeItem item in treeResponse.Tree)
                    {
                        if (item.Path?.StartsWith("mappings/") == true && item.Path.EndsWith(".json"))
                        {
                            var subject = ExtractSubjectFromPath(item.Path);

                            bool exist = await metadataDbService.SubjectExistsAsync(subject, stoppingToken);
                            if (exist) continue;

                            JsonElement mappingJson = await githubService.GetMappingJsonAsync(latestCommit.Sha, item.Path, stoppingToken);
                            RegistryItem? registryItem = MapRegistryItem(mappingJson);

                            if (registryItem == null) continue;
                            await metadataDbService.AddTokenAsync(registryItem, stoppingToken);
                        }
                    }
                    await metadataDbService.UpsertSyncStateAsync(latestCommit, stoppingToken);
                }
                else
                {
                    List<GitCommit> latestCommitsSince = await GetLatestCommitsSinceAsync(syncState.Date, stoppingToken);
                    foreach (GitCommit commit in latestCommitsSince)
                    {
                        if (string.IsNullOrEmpty(commit.Url)) continue;

                        GitCommit? resolvedCommit = await githubService.GetMappingJsonAsync<GitCommit>(commit.Url, cancellationToken: stoppingToken);
                        if (resolvedCommit is null || string.IsNullOrEmpty(resolvedCommit.Sha) || resolvedCommit.Files is null) continue;

                        foreach (GitCommitFile file in resolvedCommit.Files)
                        {
                            if (file.Filename is not null)
                            {
                                var subject = ExtractSubjectFromPath(file.Filename);

                                try
                                {
                                    JsonElement mappingJson = await githubService.GetMappingJsonAsync(resolvedCommit.Sha, file.Filename, stoppingToken);
                                    RegistryItem? registryItem = MapRegistryItem(mappingJson);
                                    if (registryItem is null) 
                                    {
                                        logger.LogWarning("Failed to map registry item for subject {Subject} in commit {Sha}", subject, resolvedCommit.Sha);
                                        continue;
                                    }

                                    bool exists = await metadataDbService.SubjectExistsAsync(subject, stoppingToken);
                                    if (exists)
                                    {
                                        await metadataDbService.UpdateTokenAsync(registryItem, stoppingToken);
                                    }
                                    else
                                    {
                                        await metadataDbService.AddTokenAsync(registryItem, stoppingToken);
                                    }
                                }
                                catch (HttpRequestException httpEx)
                                {
                                    logger.LogError(httpEx, "Network error while fetching metadata for subject {Subject}. Skipping this update.", subject);
                                }
                                catch (JsonException jsonEx)
                                {
                                    logger.LogError(jsonEx, "JSON parsing error for subject {Subject}. File may be malformed. Skipping this update.", subject);
                                }
                                catch (Exception ex)
                                {
                                    logger.LogError(ex, "Unexpected error processing metadata for subject {Subject}. Skipping this update.", subject);
                                }
                            }
                        }
                        await metadataDbService.UpsertSyncStateAsync(resolvedCommit, stoppingToken);
                    }
                }

                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "An error occurred while syncing mappings.");
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }
    }

    public RegistryItem? MapRegistryItem(JsonElement mappingJson)
    {
        try
        {
            var registryItem = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(mappingJson.GetRawText());
            if (registryItem == null)
            {
                logger.LogWarning("Failed to deserialize mapping JSON");
                return null;
            }

            // Helper method to safely extract values
            string? GetStringValue(string key) =>
                registryItem.TryGetValue(key, out var element) ? element.GetString() : null;

            string? GetValuePropertyString(string key) =>
                registryItem.TryGetValue(key, out var element) && element.TryGetProperty("value", out var valueElement)
                    ? valueElement.GetString()
                    : null;

            int GetValuePropertyInt(string key, int defaultValue = 0) =>
                registryItem.TryGetValue(key, out var element) && element.TryGetProperty("value", out var valueElement) && valueElement.TryGetInt32(out var value)
                    ? value
                    : defaultValue;

            var subject = GetStringValue("subject");
            var name = GetValuePropertyString("name");
            var ticker = GetValuePropertyString("ticker");

            // Validate required fields
            if (string.IsNullOrEmpty(subject) || string.IsNullOrEmpty(name) || string.IsNullOrEmpty(ticker))
            {
                logger.LogWarning("Invalid token data. Subject: {Subject}, Name: {Name}, Ticker: {Ticker}", 
                    subject ?? "null", name ?? "null", ticker ?? "null");
                return null;
            }

            return new RegistryItem
            {
                Subject = subject,
                Policy = GetStringValue("policy"),
                Name = name,
                Ticker = ticker,
                Description = GetValuePropertyString("description"),
                Url = GetValuePropertyString("url"),
                Logo = GetValuePropertyString("logo"),
                Decimals = GetValuePropertyInt("decimals", 0)
            };
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Error parsing registry item JSON");
            return null;
        }
    }

    private async Task<List<GitCommit>> GetLatestCommitsSinceAsync(DateTimeOffset lastSyncDate, CancellationToken stoppingToken)
    {
        var latestCommitsSince = new List<GitCommit>();
        var page = 1;

        while (true)
        {
            var commitPage = await githubService.GetCommitPageAsync(lastSyncDate, page, stoppingToken);
            if (commitPage is null || !commitPage.Any()) break;
            latestCommitsSince.AddRange(commitPage);
            page++;
        }

        return latestCommitsSince;
    }

    private static string ExtractSubjectFromPath(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return string.Empty;
        
        return path.Replace("mappings/", string.Empty)
                   .Replace(".json", string.Empty);
    }
}
