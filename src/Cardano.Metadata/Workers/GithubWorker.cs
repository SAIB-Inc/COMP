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

                            var mapping = await githubService.GetMappingJsonAsync<MetadataResponse>(latestCommit.Sha, item.Path, stoppingToken);
                            TokenMetadata? token = MapTokenMetadata(mapping);

                            if (token == null) continue;
                            await metadataDbService.AddTokenAsync(token, stoppingToken);
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
                                if (string.Equals(file.Status, "removed", StringComparison.OrdinalIgnoreCase))
                                {
                                    logger.LogInformation("Skipping removed file {Filename} in commit {Sha}", file.Filename, resolvedCommit.Sha);
                                    continue;
                                }
                                var subject = ExtractSubjectFromPath(file.Filename);

                                try
                                {
                                    var mapping = await githubService.GetMappingJsonAsync<MetadataResponse>(resolvedCommit.Sha, file.Filename, stoppingToken);
                                    TokenMetadata? token = MapTokenMetadata(mapping);
                                    if (token is null) 
                                    {
                                        logger.LogWarning("Failed to map registry item for subject {Subject} in commit {Sha}", subject, resolvedCommit.Sha);
                                        continue;
                                    }

                                    bool exists = await metadataDbService.SubjectExistsAsync(subject, stoppingToken);
                                    if (exists)
                                    {
                                        await metadataDbService.UpdateTokenAsync(token, stoppingToken);
                                    }
                                    else
                                    {
                                        await metadataDbService.AddTokenAsync(token, stoppingToken);
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

    public TokenMetadata? MapTokenMetadata(MetadataResponse? resp)
    {
        if (resp is null)
        {
            logger.LogWarning("Mapping response is null");
            return null;
        }

        var subject = resp.Subject;
        var name = resp.Name?.Value;
        var ticker = resp.Ticker?.Value;

        if (string.IsNullOrEmpty(subject) || string.IsNullOrEmpty(name) || string.IsNullOrEmpty(ticker))
        {
            logger.LogWarning("Invalid token data. Subject: {Subject}, Name: {Name}, Ticker: {Ticker}", 
                subject ?? "null", name ?? "null", ticker ?? "null");
            return null;
        }

        var decimals = resp.Decimals?.Value ?? 0;
        if (decimals < 0)
        {
            logger.LogWarning("Invalid decimals for subject {Subject}: {Decimals}", subject, decimals);
            return null;
        }

        return new TokenMetadata
        {
            Subject = subject,
            PolicyId = subject.Length >= 56 ? subject[..56] : string.Empty,
            Name = name,
            Ticker = ticker,
            Description = resp.Description?.Value,
            Url = resp.Url?.Value,
            Logo = resp.Logo?.Value,
            Policy = resp.Policy,
            Decimals = decimals
        };
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
