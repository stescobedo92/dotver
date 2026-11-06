using System.Security.Cryptography;
using System.Text.Json;
using DotVer.Domain;

namespace DotVer.Application;

public interface ISdkDownloadService
{
    /// <summary>Resolves every (token, RID) pair to a published archive without downloading anything.</summary>
    Task<DownloadBatchResult> PlanAsync(IReadOnlyList<string> tokens, IReadOnlyList<string> rids, string outputDirectory, CancellationToken cancellationToken);

    /// <summary>Plans, then downloads and verifies each archive into the output directory; one failure never stops the rest.</summary>
    Task<DownloadBatchResult> DownloadAsync(
        IReadOnlyList<string> tokens,
        IReadOnlyList<string> rids,
        string outputDirectory,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken);
}

/// <summary>
/// Downloads official SDK archives for several versions and several RIDs in
/// one go, the way the host and the install scripts identify them: a token
/// (version, channel, lts, sts, latest) resolves to an exact SDK version, the
/// channel's releases.json lists one archive per RID, and the archive is only
/// kept after its published SHA-512 matches. The result pairs with
/// <c>dotver install --from</c> for air-gapped machines.
/// </summary>
public sealed class SdkDownloadService : ISdkDownloadService
{
    public const long MaxArchiveBytes = 2L * 1024 * 1024 * 1024;

    private const int MaxReleases = 500;
    private const int MaxSdksPerRelease = 32;
    private const int MaxFilesPerSdk = 128;
    private const int MaxFileNameLength = 200;

    private readonly IInstallService _installService;
    private readonly IHttpResourceClient _httpResourceClient;
    private readonly IEnvironmentContext _environment;

    public SdkDownloadService(IInstallService installService, IHttpResourceClient httpResourceClient, IEnvironmentContext environment)
    {
        _installService = installService;
        _httpResourceClient = httpResourceClient;
        _environment = environment;
    }

    public async Task<DownloadBatchResult> PlanAsync(IReadOnlyList<string> tokens, IReadOnlyList<string> rids, string outputDirectory, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(rids);

        var requestedTokens = tokens
            .Select(static token => token?.Trim() ?? string.Empty)
            .Where(static token => token.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (requestedTokens.Count == 0)
        {
            throw new InvalidOperationException("Pass at least one version or channel: dotver download 8.0 9.0 --rid linux-x64");
        }

        // RIDs are user input: reject them before touching the network.
        var platforms = (rids.Count == 0 ? [HostPlatformResolver.Resolve(_environment).Rid] : rids)
            .Select(static rid => rid?.Trim() ?? string.Empty)
            .Where(static rid => rid.Length > 0)
            .Select(HostPlatformResolver.FromRid)
            .DistinctBy(static platform => platform.Rid, StringComparer.Ordinal)
            .ToList();
        if (platforms.Count == 0)
        {
            throw new InvalidOperationException("Pass at least one RID with --rid, or none to use this machine's RID.");
        }

        var directory = ResolveOutputDirectory(outputDirectory);
        var channels = await _installService.ListRemoteAsync(cancellationToken).ConfigureAwait(false);
        var channelFiles = new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<ArchiveFile>>>(StringComparer.OrdinalIgnoreCase);
        var entries = new List<DownloadBatchEntry>();

        foreach (var token in requestedTokens)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string version;
            ReleaseChannel channel;
            IReadOnlyList<ArchiveFile> files;
            try
            {
                var plan = await _installService.ResolveInstallPlanAsync(token, cancellationToken).ConfigureAwait(false);
                version = plan.ResolvedVersion;
                channel = plan.Channel ?? FindChannel(channels, version);
                if (!channelFiles.TryGetValue(channel.Channel, out var sdks))
                {
                    sdks = await LoadChannelSdksAsync(channel, cancellationToken).ConfigureAwait(false);
                    channelFiles[channel.Channel] = sdks;
                }

                if (!sdks.TryGetValue(version, out files!))
                {
                    throw new InvalidOperationException(
                        $"The {channel.Channel} release metadata does not publish SDK {version}; run 'dotver list-remote' to see what is available.");
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException or IOException)
            {
                foreach (var platform in platforms)
                {
                    entries.Add(new DownloadBatchEntry(token, platform.Rid, null, null, null, exception.Message));
                }

                continue;
            }

            foreach (var platform in platforms)
            {
                var archive = SelectArchive(files, version, platform);
                entries.Add(archive is null
                    ? new DownloadBatchEntry(token, platform.Rid, version, null, null,
                        $"The {channel.Channel} release metadata has no {platform.ArchiveExtension} archive of SDK {version} for {platform.Rid}.")
                    : new DownloadBatchEntry(token, platform.Rid, version, archive, null, null));
            }
        }

        return new DownloadBatchResult(directory, entries);
    }

    public async Task<DownloadBatchResult> DownloadAsync(
        IReadOnlyList<string> tokens,
        IReadOnlyList<string> rids,
        string outputDirectory,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var plan = await PlanAsync(tokens, rids, outputDirectory, cancellationToken).ConfigureAwait(false);
        Directory.CreateDirectory(plan.OutputDirectory);

        var entries = new List<DownloadBatchEntry>(plan.Entries.Count);
        foreach (var entry in plan.Entries)
        {
            if (!entry.Planned)
            {
                entries.Add(entry);
                continue;
            }

            try
            {
                var download = await FetchAsync(entry.Archive!, plan.OutputDirectory, progress, cancellationToken).ConfigureAwait(false);
                entries.Add(entry with { Download = download });
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException or HttpRequestException)
            {
                entries.Add(entry with { Error = exception.Message });
            }
        }

        return new DownloadBatchResult(plan.OutputDirectory, entries);
    }

    private string ResolveOutputDirectory(string outputDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        var directory = Path.GetFullPath(outputDirectory.Trim(), _environment.CurrentDirectory);

        // Never write through a pre-planted link/junction.
        if (Directory.Exists(directory) && (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException($"Refusing to download into a symlinked directory: '{TerminalText.Sanitize(directory)}'.");
        }

        return directory;
    }

    private static ReleaseChannel FindChannel(IReadOnlyList<ReleaseChannel> channels, string version)
    {
        var parsed = SdkVersion.Parse(version);
        var name = $"{parsed.Major}.{parsed.Minor}";
        return channels.FirstOrDefault(channel => string.Equals(channel.Channel, name, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"No release channel publishes SDK {version} (channel {name} is not in the releases index).");
    }

    private async Task<IReadOnlyDictionary<string, IReadOnlyList<ArchiveFile>>> LoadChannelSdksAsync(ReleaseChannel channel, CancellationToken cancellationToken)
    {
        var url = channel.ReleasesJsonUrl
            ?? throw new InvalidOperationException($"The releases index has no releases.json link for channel {channel.Channel}.");

        // The transport only talks https to the allow-listed Microsoft hosts
        // (or the configured mirror), so a poisoned index cannot redirect this.
        var payload = await _httpResourceClient.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
        return ParseChannelSdks(payload, channel.Channel);
    }

    /// <summary>Exact SDK version to its published files; remote metadata is untrusted and every field is re-validated.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<ArchiveFile>> ParseChannelSdks(string payload, string channel)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(payload);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"The releases.json of channel {channel} is not valid JSON.", exception);
        }

        var result = new Dictionary<string, IReadOnlyList<ArchiveFile>>(StringComparer.OrdinalIgnoreCase);
        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("releases", out var releases) ||
                releases.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException($"The releases.json of channel {channel} is malformed: expected an object with a releases array.");
            }

            var releaseCount = 0;
            foreach (var release in releases.EnumerateArray())
            {
                if (++releaseCount > MaxReleases)
                {
                    break;
                }

                if (release.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var sdkCount = 0;
                if (release.TryGetProperty("sdks", out var sdks) && sdks.ValueKind == JsonValueKind.Array)
                {
                    foreach (var sdk in sdks.EnumerateArray())
                    {
                        if (++sdkCount > MaxSdksPerRelease)
                        {
                            break;
                        }

                        AddSdk(result, sdk);
                    }
                }

                // Older channels publish a singular `sdk` instead of `sdks`.
                if (release.TryGetProperty("sdk", out var singleSdk))
                {
                    AddSdk(result, singleSdk);
                }
            }
        }

        return result;
    }

    private static void AddSdk(Dictionary<string, IReadOnlyList<ArchiveFile>> result, JsonElement sdk)
    {
        if (sdk.ValueKind != JsonValueKind.Object ||
            !SdkVersion.TryParse(ReadString(sdk, "version"), out var version) ||
            result.ContainsKey(version!.Raw) ||
            !sdk.TryGetProperty("files", out var files) ||
            files.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var archives = new List<ArchiveFile>();
        foreach (var file in files.EnumerateArray())
        {
            if (archives.Count >= MaxFilesPerSdk)
            {
                break;
            }

            if (file.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var name = ReadString(file, "name");
            var rid = ReadString(file, "rid");
            var url = ReadString(file, "url");
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(rid) || string.IsNullOrWhiteSpace(url) ||
                !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            archives.Add(new ArchiveFile(name.Trim(), rid.Trim(), uri.AbsoluteUri, NormalizeSha512(ReadString(file, "hash"))));
        }

        result[version.Raw] = archives;
    }

    private static SdkArchive? SelectArchive(IReadOnlyList<ArchiveFile> files, string version, HostPlatform platform)
    {
        var file = files.FirstOrDefault(candidate =>
            string.Equals(candidate.Rid, platform.Rid, StringComparison.OrdinalIgnoreCase) &&
            candidate.Name.EndsWith(platform.ArchiveExtension, StringComparison.OrdinalIgnoreCase));
        if (file is null)
        {
            return null;
        }

        // The local name comes from the URL (dotnet-sdk-8.0.427-linux-x64.tar.gz);
        // anything that is not a plain file name falls back to a name built from
        // values that were already validated.
        var fromUrl = Uri.UnescapeDataString(new Uri(file.Url).Segments[^1]);
        var fileName = IsSafeArchiveName(fromUrl, platform.ArchiveExtension)
            ? fromUrl
            : $"dotnet-sdk-{version}-{platform.Rid}{platform.ArchiveExtension}";

        return new SdkArchive(version, platform.Rid, fileName, file.Url, file.Sha512);
    }

    public static bool IsSafeArchiveName(string name, string extension) =>
        !string.IsNullOrEmpty(name) &&
        name.Length <= MaxFileNameLength &&
        name.EndsWith(extension, StringComparison.OrdinalIgnoreCase) &&
        !name.StartsWith('.') &&
        string.Equals(Path.GetFileName(name), name, StringComparison.Ordinal) &&
        name.All(static c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-');

    private static string? NormalizeSha512(string? hash)
    {
        if (string.IsNullOrWhiteSpace(hash))
        {
            return null;
        }

        var value = hash.Trim().ToLowerInvariant();
        return value.Length == 128 && value.All(static c => char.IsAsciiHexDigit(c)) ? value : null;
    }

    private static string? ReadString(JsonElement node, string name) =>
        node.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private async Task<SdkArchiveDownload> FetchAsync(SdkArchive archive, string directory, IProgress<DownloadProgress>? progress, CancellationToken cancellationToken)
    {
        if (archive.Sha512 is null)
        {
            throw new InvalidOperationException(
                $"{archive.FileName}: the release metadata publishes no SHA-512 for this archive; refusing to download it unverified.");
        }

        var destination = Path.Combine(directory, archive.FileName);
        if (File.Exists(destination) &&
            string.Equals(await ComputeSha512Async(destination, cancellationToken).ConfigureAwait(false), archive.Sha512, StringComparison.Ordinal))
        {
            WriteSidecar(destination, archive);
            return new SdkArchiveDownload(archive, destination, new FileInfo(destination).Length, SkippedExisting: true);
        }

        // Stream into a sibling .part file so the final name only ever holds a verified archive.
        var partial = destination + ".part";
        long bytes;
        try
        {
            bytes = await _httpResourceClient.DownloadToFileAsync(
                archive.Url,
                partial,
                MaxArchiveBytes,
                (received, total) => progress?.Report(new DownloadProgress(archive.FileName, received, total)),
                cancellationToken).ConfigureAwait(false);

            var actual = await ComputeSha512Async(partial, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(actual, archive.Sha512, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Checksum mismatch for {archive.FileName}: expected sha512 {archive.Sha512}, got {actual}. The download was discarded.");
            }

            File.Move(partial, destination, overwrite: true);
        }
        finally
        {
            DeleteQuietly(partial);
        }

        WriteSidecar(destination, archive);
        return new SdkArchiveDownload(archive, destination, bytes, SkippedExisting: false);
    }

    // <archive>.sha512 in the `sha512sum -c` layout, next to the archive, so the
    // digest travels with the file and `dotver install --from` can be scripted.
    private static void WriteSidecar(string destination, SdkArchive archive) =>
        File.WriteAllText(destination + ".sha512", archive.Sha512 + "  " + archive.FileName + "\n");

    private static async Task<string> ComputeSha512Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(await SHA512.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
    }

    private static void DeleteQuietly(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Best effort: a leftover .part is never mistaken for a verified archive.
        }
    }

    /// <summary>One file entry of releases.json, already validated (https URL, non-empty name and RID).</summary>
    public sealed record ArchiveFile(string Name, string Rid, string Url, string? Sha512);
}
