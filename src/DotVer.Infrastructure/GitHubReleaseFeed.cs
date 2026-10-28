using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DotVer.Application;
using DotVer.Domain;

namespace DotVer.Infrastructure;

/// <summary>
/// Reads dotver releases from the GitHub REST API and downloads their assets
/// through the same narrow transport as every other download: https only,
/// GitHub hosts only (API, release pages and the asset CDN), capped sizes.
/// </summary>
public sealed partial class GitHubReleaseFeed : IReleaseFeed
{
    private const long MaxAssetBytes = 256L * 1024 * 1024;
    private const long MaxMetadataBytes = 4 * 1024 * 1024;

    private static readonly string[] Hosts =
    [
        "api.github.com",
        "github.com",
        "objects.githubusercontent.com",
        "release-assets.githubusercontent.com"
    ];

    private readonly HttpResourceClient _client;

    public GitHubReleaseFeed(HttpClient httpClient)
    {
        httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        httpClient.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        _client = new HttpResourceClient(httpClient, MaxAssetBytes, sources: null, allowedHosts: Hosts);
    }

    public async Task<ReleaseInfo> GetLatestAsync(string repository, CancellationToken cancellationToken) =>
        Parse(await FetchJsonAsync($"https://api.github.com/repos/{ValidateRepository(repository)}/releases/latest", cancellationToken).ConfigureAwait(false));

    public async Task<ReleaseInfo> GetByTagAsync(string repository, string tag, CancellationToken cancellationToken) =>
        Parse(await FetchJsonAsync(
            $"https://api.github.com/repos/{ValidateRepository(repository)}/releases/tags/{Uri.EscapeDataString(tag)}",
            cancellationToken).ConfigureAwait(false));

    public async Task<byte[]> DownloadAssetAsync(string url, long maxBytes, CancellationToken cancellationToken)
    {
        var bytes = await _client.GetBytesAsync(url, cancellationToken).ConfigureAwait(false);
        return bytes.LongLength <= maxBytes
            ? bytes
            : throw new InvalidOperationException("The release asset is larger than expected; refusing to use it.");
    }

    private async Task<string> FetchJsonAsync(string url, CancellationToken cancellationToken)
    {
        var bytes = await _client.GetBytesAsync(url, cancellationToken).ConfigureAwait(false);
        if (bytes.LongLength > MaxMetadataBytes)
        {
            throw new InvalidOperationException("Release metadata is unexpectedly large; refusing to read it.");
        }

        return Encoding.UTF8.GetString(bytes);
    }

    internal static ReleaseInfo Parse(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("tag_name", out var tagNode) || tagNode.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException("The release metadata has no tag_name.");
        }

        var tag = tagNode.GetString()!;
        var assets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (root.TryGetProperty("assets", out var assetsNode) && assetsNode.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assetsNode.EnumerateArray())
            {
                if (asset.ValueKind == JsonValueKind.Object &&
                    asset.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String &&
                    asset.TryGetProperty("browser_download_url", out var url) && url.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(name.GetString()) && !string.IsNullOrWhiteSpace(url.GetString()))
                {
                    assets[name.GetString()!] = url.GetString()!;
                }
            }
        }

        return new ReleaseInfo(tag, tag.TrimStart('v', 'V'), assets);
    }

    private static string ValidateRepository(string repository) =>
        RepositoryPattern().IsMatch(repository)
            ? repository
            : throw new InvalidOperationException("The release repository must look like owner/repo.");

    [GeneratedRegex(@"\A[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+\z", RegexOptions.CultureInvariant)]
    private static partial Regex RepositoryPattern();
}
