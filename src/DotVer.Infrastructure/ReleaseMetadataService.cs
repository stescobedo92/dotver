using System.Text;
using System.Text.Json;
using DotVer.Application;
using DotVer.Domain;

namespace DotVer.Infrastructure;

/// <summary>
/// Fetches official .NET release metadata with a dedicated HTTP client:
/// redirects are never followed and payloads are size-capped while streaming,
/// so a compromised/poisoned endpoint cannot bounce requests to internal
/// services or exhaust memory.
/// </summary>
public sealed class ReleaseMetadataService : IReleaseMetadataService
{
    private const string ReleasesIndexUrl = "https://builds.dotnet.microsoft.com/dotnet/release-metadata/releases-index.json";
    private const long MaxIndexBytes = 4 * 1024 * 1024;
    private const long MaxDetailBytes = 32 * 1024 * 1024;
    private const int MaxReleases = 500;
    private const int MaxSdksPerRelease = 32;
    private const int MaxCvesPerRelease = 512;

    private readonly HttpClient _httpClient;

    public ReleaseMetadataService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<ChannelRelease>> ListChannelReleasesAsync(string channel, CancellationToken cancellationToken)
    {
        var indexPayload = await GetCappedStringAsync(ReleasesIndexUrl, MaxIndexBytes, cancellationToken).ConfigureAwait(false);
        var detailUrl = FindReleasesJsonUrl(indexPayload, channel);
        if (detailUrl is null)
        {
            return [];
        }

        // The index is remote input: only ever fetch release detail from the
        // official Microsoft builds host over HTTPS, default port, no userinfo.
        if (!Uri.TryCreate(detailUrl, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(uri.Host, "builds.dotnet.microsoft.com", StringComparison.OrdinalIgnoreCase) ||
            !uri.IsDefaultPort ||
            !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new InvalidOperationException("Refusing to fetch release metadata from an untrusted location.");
        }

        var payload = await GetCappedStringAsync(uri.ToString(), MaxDetailBytes, cancellationToken).ConfigureAwait(false);
        return ParseChannelReleases(payload);
    }

    private async Task<string> GetCappedStringAsync(string url, long maxBytes, CancellationToken cancellationToken)
    {
        using var response = await _httpClient
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentLength is > 0 and var declared && declared > maxBytes)
        {
            throw new InvalidOperationException("Release metadata payload is unexpectedly large; refusing to read it.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > maxBytes)
            {
                throw new InvalidOperationException("Release metadata payload is unexpectedly large; refusing to read it.");
            }

            buffer.Write(chunk, 0, read);
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static string? FindReleasesJsonUrl(string indexPayload, string channel)
    {
        using var document = JsonDocument.Parse(indexPayload);
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("releases-index", out var index) ||
            index.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var node in index.EnumerateArray())
        {
            if (node.ValueKind == JsonValueKind.Object &&
                node.TryGetProperty("channel-version", out var version) &&
                version.ValueKind == JsonValueKind.String &&
                string.Equals(version.GetString(), channel, StringComparison.OrdinalIgnoreCase) &&
                node.TryGetProperty("releases.json", out var url) &&
                url.ValueKind == JsonValueKind.String)
            {
                return url.GetString();
            }
        }

        return null;
    }

    internal static IReadOnlyList<ChannelRelease> ParseChannelReleases(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        var releases = new List<ChannelRelease>();
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("releases", out var releasesNode) ||
            releasesNode.ValueKind != JsonValueKind.Array)
        {
            return releases;
        }

        foreach (var release in releasesNode.EnumerateArray())
        {
            if (releases.Count >= MaxReleases)
            {
                break;
            }

            if (release.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var releaseVersion = release.TryGetProperty("release-version", out var rv) && rv.ValueKind == JsonValueKind.String
                ? rv.GetString() ?? "unknown"
                : "unknown";
            if (releaseVersion.Length > 64)
            {
                releaseVersion = "unknown";
            }

            var isSecurity = release.TryGetProperty("security", out var security) && security.ValueKind == JsonValueKind.True;

            var sdkVersions = new List<string>();
            if (release.TryGetProperty("sdks", out var sdks) && sdks.ValueKind == JsonValueKind.Array)
            {
                foreach (var sdk in sdks.EnumerateArray())
                {
                    if (sdkVersions.Count >= MaxSdksPerRelease)
                    {
                        break;
                    }

                    AddSdkVersion(sdkVersions, sdk);
                }
            }

            // Older channels (e.g. 2.1) publish a singular `sdk` instead of `sdks`.
            if (sdkVersions.Count == 0 && release.TryGetProperty("sdk", out var singleSdk))
            {
                AddSdkVersion(sdkVersions, singleSdk);
            }

            var cveIds = new List<string>();
            if (isSecurity && release.TryGetProperty("cve-list", out var cves) && cves.ValueKind == JsonValueKind.Array)
            {
                foreach (var cve in cves.EnumerateArray())
                {
                    if (cveIds.Count >= MaxCvesPerRelease)
                    {
                        break;
                    }

                    if (cve.ValueKind == JsonValueKind.Object &&
                        cve.TryGetProperty("cve-id", out var id) &&
                        id.ValueKind == JsonValueKind.String &&
                        IsValidCveId(id.GetString()))
                    {
                        cveIds.Add(id.GetString()!);
                    }
                }
            }

            releases.Add(new ChannelRelease(releaseVersion, isSecurity, sdkVersions, cveIds));
        }

        return releases;
    }

    private static void AddSdkVersion(List<string> versions, JsonElement sdk)
    {
        if (sdk.ValueKind == JsonValueKind.Object &&
            sdk.TryGetProperty("version", out var v) &&
            v.ValueKind == JsonValueKind.String &&
            SdkVersion.TryParse(v.GetString(), out var parsed))
        {
            versions.Add(parsed!.Raw);
        }
    }

    private static bool IsValidCveId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 32 || !value.StartsWith("CVE-", StringComparison.Ordinal))
        {
            return false;
        }

        var parts = value.Split('-');
        return parts.Length == 3 &&
               parts[1].Length == 4 && parts[1].All(char.IsAsciiDigit) &&
               parts[2].Length is >= 4 and <= 10 && parts[2].All(char.IsAsciiDigit);
    }
}
