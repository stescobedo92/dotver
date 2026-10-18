using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotVer.Domain;

namespace DotVer.Application;

/// <summary>
/// Best-effort on-disk cache for inventory scans. Only (root, version) pairs
/// are persisted — never trust-bearing fields. On load, every pair is
/// validated against the CURRENT candidate roots and the filesystem, and the
/// caller re-derives every other field (paths, origin, managed flag,
/// architecture, selectability) exactly as a real scan does, so a tampered
/// cache cannot invent roots, launder architectures or smuggle paths.
/// Set DOTVER_NO_CACHE=1 to disable.
/// </summary>
internal sealed class InventoryCache
{
    private const int SchemaVersion = 2;
    private const int MaxEntries = 512;
    private const long MaxCacheBytes = 4 * 1024 * 1024;
    private static readonly TimeSpan MaxAge = TimeSpan.FromHours(24);
    private static readonly TimeSpan MaxClockSkew = TimeSpan.FromMinutes(5);

    private readonly DotVerPaths _paths;
    private readonly IEnvironmentContext _environment;

    public InventoryCache(DotVerPaths paths, IEnvironmentContext environment)
    {
        _paths = paths;
        _environment = environment;
    }

    public bool Enabled =>
        !string.Equals(_environment.GetEnvironmentVariable("DOTVER_NO_CACHE"), "1", StringComparison.Ordinal);

    private string CacheDirectory => Path.Combine(_paths.Home, "cache");
    private string CacheFile => Path.Combine(CacheDirectory, "inventory.json");

    private StringComparer PathComparer => _environment.IsWindows
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    /// <summary>
    /// Cheap stat-only fingerprint of everything that can change the scan
    /// result: the candidate roots plus the modification times of each
    /// root's sdk directory and host file.
    /// </summary>
    public string ComputeFingerprint(IReadOnlyCollection<string> roots)
    {
        var builder = new StringBuilder();
        builder.Append(_environment.RuntimeArchitecture).Append('|');
        builder.Append(_paths.ManagedInstalls).Append('|');
        builder.Append(_environment.GetEnvironmentVariable("DOTNET_ROOT") ?? string.Empty).Append('|');
        foreach (var entry in _environment.GetPathEntries())
        {
            builder.Append(entry).Append(';');
        }

        builder.Append('|');
        foreach (var root in roots.OrderBy(static r => r, StringComparer.Ordinal))
        {
            builder.Append(root).Append('#');
            builder.Append(Ticks(Path.Combine(root, "sdk"))).Append('#');
            builder.Append(Ticks(Path.Combine(root, _environment.IsWindows ? "dotnet.exe" : "dotnet"))).Append(';');
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexString(hash);
    }

    /// <summary>
    /// Returns validated (root, version) pairs, or null to force a real scan.
    /// A pair is only accepted when its normalized root is one of the current
    /// candidate roots and the expected on-disk layout still exists.
    /// </summary>
    public IReadOnlyList<(string Root, SdkVersion Version)>? TryLoadPairs(
        string fingerprint,
        IReadOnlyCollection<string> candidateRoots)
    {
        if (!Enabled)
        {
            return null;
        }

        try
        {
            var info = new FileInfo(CacheFile);
            if (!info.Exists)
            {
                return null;
            }

            var age = DateTime.UtcNow - info.LastWriteTimeUtc;
            if (age > MaxAge || age < -MaxClockSkew)
            {
                return null;
            }

            using var stream = new FileStream(CacheFile, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > MaxCacheBytes)
            {
                return null;
            }

            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;
            if (!root.TryGetProperty("schemaVersion", out var schema) || schema.GetInt32() != SchemaVersion ||
                !root.TryGetProperty("fingerprint", out var stored) ||
                !string.Equals(stored.GetString(), fingerprint, StringComparison.Ordinal) ||
                !root.TryGetProperty("sdks", out var sdks) || sdks.ValueKind != JsonValueKind.Array ||
                sdks.GetArrayLength() > MaxEntries)
            {
                return null;
            }

            var candidates = new HashSet<string>(candidateRoots, PathComparer);
            var pairs = new List<(string Root, SdkVersion Version)>();
            var seen = new HashSet<string>(PathComparer);
            foreach (var node in sdks.EnumerateArray())
            {
                var rawRoot = GetString(node, "root");
                var rawVersion = GetString(node, "version");
                if (rawRoot is null || rawVersion is null ||
                    rawRoot.Length == 0 || rawRoot.Any(char.IsControl) ||
                    !Path.IsPathFullyQualified(rawRoot) ||
                    !SdkVersion.TryParse(rawVersion, out var version))
                {
                    return null;
                }

                // Normalize and require membership in the CURRENT root set:
                // the cache can never introduce a root a real scan would not
                // consider right now.
                var normalizedRoot = Path.GetFullPath(rawRoot.Trim());
                if (!candidates.TryGetValue(normalizedRoot, out var canonicalRoot))
                {
                    return null;
                }

                var hostFile = Path.Combine(canonicalRoot, _environment.IsWindows ? "dotnet.exe" : "dotnet");
                var sdkDirectory = Path.Combine(canonicalRoot, "sdk", version!.Raw);
                if (!File.Exists(hostFile) || !Directory.Exists(sdkDirectory))
                {
                    return null;
                }

                if (seen.Add(canonicalRoot + "|" + version.Raw))
                {
                    pairs.Add((canonicalRoot, version));
                }
            }

            // Completeness: the cached set must match what a real scan would
            // find right now, per root. Without this, simply omitting pairs
            // could hide SDKs or flip derived facts (e.g. a non-managed root
            // with two SDKs reduced to one would become "directly selectable").
            // Ordinal (exact case) on purpose: a legitimate cache stores the
            // exact directory names the real scan enumerated. Case-insensitive
            // sets would collapse case-twin directories on case-sensitive
            // filesystems and let an omission slip through.
            var cachedByRoot = pairs
                .GroupBy(pair => pair.Root, PathComparer)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(pair => pair.Version.Raw).ToHashSet(StringComparer.Ordinal),
                    PathComparer);
            foreach (var candidate in candidates)
            {
                var sdkDirectory = Path.Combine(candidate, "sdk");
                var actual = Directory.Exists(sdkDirectory)
                    ? Directory.EnumerateDirectories(sdkDirectory)
                        .Select(Path.GetFileName)
                        .Where(static name => SdkVersion.TryParse(name, out _))
                        .Select(static name => name!)
                        .ToHashSet(StringComparer.Ordinal)
                    : [];
                var cachedForRoot = cachedByRoot.TryGetValue(candidate, out var set) ? set : [];
                if (!actual.SetEquals(cachedForRoot))
                {
                    return null;
                }
            }

            return pairs;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or FormatException or ArgumentException)
        {
            return null;
        }
    }

    public void Store(string fingerprint, IReadOnlyList<SdkInstallation> installations)
    {
        if (!Enabled || installations.Count > MaxEntries)
        {
            return;
        }

        string? tempFile = null;
        try
        {
            Directory.CreateDirectory(CacheDirectory);
            var items = new JsonArray();
            foreach (var sdk in installations)
            {
                items.Add(new JsonObject
                {
                    ["root"] = sdk.HostRoot,
                    ["version"] = sdk.Version.Raw
                });
            }

            var payload = new JsonObject
            {
                ["schemaVersion"] = SchemaVersion,
                ["fingerprint"] = fingerprint,
                ["sdks"] = items
            };

            tempFile = CacheFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(tempFile, payload.ToJsonString());
            File.Move(tempFile, CacheFile, overwrite: true);
            tempFile = null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Cache is best-effort; never fail a scan because of it.
        }
        finally
        {
            if (tempFile is not null)
            {
                try
                {
                    File.Delete(tempFile);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
    }

    private static string? GetString(JsonElement node, string property) =>
        node.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static long Ticks(string path)
    {
        try
        {
            return File.GetLastWriteTimeUtc(path).Ticks;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return 0;
        }
    }
}
