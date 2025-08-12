using System.Text.Json;
using Cardano.Metadata.Models.Github;

namespace Cardano.Metadata.Services;

public class GithubService
(
   IConfiguration config,
   IHttpClientFactory httpClientFactory)
{
    private readonly string _registryOwner = config["RegistryOwner"] ?? throw new InvalidOperationException("RegistryOwner is not configured.");
    private readonly string _registryRepo = config["RegistryRepo"] ?? throw new InvalidOperationException("RegistryRepo is not configured.");
    private readonly HttpClient _httpClient = httpClientFactory.CreateClient("GithubApi");

    public async Task<GitCommit?> GetCommitsAsync(CancellationToken cancellationToken)
    {
        string commitsUrl = $"https://api.github.com/repos/{_registryOwner}/{_registryRepo}/commits";
        IEnumerable<GitCommit>? commits = await _httpClient.GetFromJsonAsync<IEnumerable<GitCommit>>(commitsUrl, cancellationToken);
        return commits?.FirstOrDefault();
    }

    public async Task<GitTreeResponse?> GetGitTreeAsync(string commitSha, CancellationToken cancellationToken)
    {
        string treeUrl = $"https://api.github.com/repos/{_registryOwner}/{_registryRepo}/git/trees/{commitSha}?recursive=true";
        GitTreeResponse? gitTreeResponse = await _httpClient.GetFromJsonAsync<GitTreeResponse>(treeUrl, cancellationToken);
        return gitTreeResponse;
    }

    public async Task<JsonElement> GetMappingJsonAsync(string commitSha, string filePath, CancellationToken cancellationToken)
    {
        string rawUrl = $"https://raw.githubusercontent.com/{_registryOwner}/{_registryRepo}/{commitSha}/{filePath}";
        return await GetMappingJsonAsync<JsonElement>(rawUrl, cancellationToken);
    }

    public async Task<T?> GetMappingJsonAsync<T>(string rawUrl, CancellationToken cancellationToken)
    {
        return await _httpClient.GetFromJsonAsync<T>(rawUrl, cancellationToken: cancellationToken);
    }

    public async Task<IEnumerable<GitCommit>?> GetCommitPageAsync(DateTimeOffset lastSync, int page, CancellationToken cancellationToken)
    {

        string commitsUrl = $"https://api.github.com/repos/{_registryOwner}/{_registryRepo}/commits?since={lastSync.AddSeconds(1):yyyy-MM-dd'T'HH:mm:ssZ}&page={page}";
        IEnumerable<GitCommit>? commitPage = await _httpClient.GetFromJsonAsync<IEnumerable<GitCommit>>(commitsUrl, cancellationToken);
        return commitPage;
    }
}
