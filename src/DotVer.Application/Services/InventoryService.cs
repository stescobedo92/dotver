using DotVer.Domain;

namespace DotVer.Application;

public sealed class InventoryService : IInventoryService
{
    private readonly DotVerPaths _paths;
    private readonly IEnvironmentContext _environment;
    private readonly IConfigStore _configStore;

    public InventoryService(DotVerPaths paths, IEnvironmentContext environment, IConfigStore configStore)
    {
        _paths = paths;
        _environment = environment;
        _configStore = configStore;
    }

    public Task<IReadOnlyList<SdkInstallation>> ScanAsync(CancellationToken cancellationToken) =>
        ScanAsync(forceRefresh: false, cancellationToken);

    public async Task<IReadOnlyList<SdkInstallation>> ScanAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        var config = await _configStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        var comparer = _environment.IsWindows
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

        var rootHints = new Dictionary<string, (SdkOriginKind Origin, string Source, bool IsManaged)>(comparer);

        void RegisterRoot(string path, SdkOriginKind origin, string source, bool isManaged)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            // A relative hint (`.` on PATH, `register-root .`, DOTNET_ROOT=sdk)
            // would resolve against whatever directory dotver runs in, letting a
            // cloned repository masquerade as a dotnet root.
            if (!Path.IsPathFullyQualified(path.Trim()))
            {
                return;
            }

            var normalized = Path.GetFullPath(path.Trim());
            if (!Directory.Exists(normalized))
            {
                return;
            }

            if (!rootHints.ContainsKey(normalized))
            {
                rootHints[normalized] = (origin, source, isManaged);
            }
        }

        if (Directory.Exists(_paths.ManagedInstalls))
        {
            foreach (var dir in Directory.EnumerateDirectories(_paths.ManagedInstalls))
            {
                RegisterRoot(dir, SdkOriginKind.ManagedInstall, "dotver-managed", isManaged: true);
            }
        }

        foreach (var knownRoot in _environment.GetKnownDotnetRoots())
        {
            RegisterRoot(knownRoot, SdkOriginKind.OfficialInstallRoot, "known-root", isManaged: false);
        }

        foreach (var registeredRoot in config.RegisteredRoots)
        {
            RegisterRoot(registeredRoot, SdkOriginKind.RegisteredRoot, "config", isManaged: false);
        }

        var dotnetRoot = _environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrWhiteSpace(dotnetRoot))
        {
            RegisterRoot(dotnetRoot, SdkOriginKind.CustomPath, "DOTNET_ROOT", isManaged: false);
        }

        foreach (var pathEntry in _environment.GetPathEntries())
        {
            RegisterRoot(pathEntry, SdkOriginKind.PathProbe, "PATH", isManaged: false);
        }

        var cache = new InventoryCache(_paths, _environment);
        string? fingerprint = null;
        if (!forceRefresh && cache.Enabled)
        {
            fingerprint = cache.ComputeFingerprint(rootHints.Keys);
            if (cache.TryLoadPairs(fingerprint, rootHints.Keys) is { } pairs)
            {
                // Re-derive every trust-bearing field from the current root
                // hints exactly as the real scan below does.
                var byRoot = pairs
                    .GroupBy(pair => pair.Root, comparer)
                    .ToDictionary(
                        group => group.Key,
                        group => group.Select(pair => pair.Version).OrderByDescending(static v => v).ToList(),
                        comparer);
                var cachedInstallations = new List<SdkInstallation>();
                foreach (var entry in rootHints)
                {
                    if (byRoot.TryGetValue(entry.Key, out var versions) &&
                        BuildInstallations(entry, versions) is { } built)
                    {
                        cachedInstallations.AddRange(built);
                    }
                }

                return OrderInstallations(cachedInstallations, comparer);
            }
        }

        var rawInstallations = new List<SdkInstallation>();
        foreach (var entry in rootHints)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var sdkDirectory = Path.Combine(entry.Key, "sdk");
            if (!Directory.Exists(sdkDirectory))
            {
                continue;
            }

            var versions = Directory
                .EnumerateDirectories(sdkDirectory)
                .Select(Path.GetFileName)
                .Where(static name => SdkVersion.TryParse(name, out _))
                .Select(static name => SdkVersion.Parse(name!))
                .OrderByDescending(static version => version)
                .ToList();

            if (BuildInstallations(entry, versions) is { } built)
            {
                rawInstallations.AddRange(built);
            }
        }

        var installations = OrderInstallations(rawInstallations, comparer);

        if (fingerprint is not null)
        {
            cache.Store(fingerprint, installations);
        }

        return installations;
    }

    private List<SdkInstallation>? BuildInstallations(
        KeyValuePair<string, (SdkOriginKind Origin, string Source, bool IsManaged)> entry,
        IReadOnlyList<SdkVersion> versions)
    {
        var hostRoot = entry.Key;
        var dotnetPath = ResolveDotnetPath(hostRoot);
        if (dotnetPath is null)
        {
            return null;
        }

        var sdkDirectory = Path.Combine(hostRoot, "sdk");
        var directlySelectable = entry.Value.IsManaged || versions.Count == 1;
        var architecture = entry.Value.IsManaged ? ParseManagedArchitecture(hostRoot) : null;
        return versions
            .Select(version => new SdkInstallation(
                version,
                hostRoot,
                Path.Combine(sdkDirectory, version.Raw),
                dotnetPath,
                entry.Value.Origin,
                BuildOriginHint(entry.Value.Origin, hostRoot, entry.Value.Source),
                entry.Value.IsManaged,
                directlySelectable,
                entry.Value.Source,
                architecture))
            .ToList();
    }

    private List<SdkInstallation> OrderInstallations(List<SdkInstallation> rawInstallations, StringComparer comparer) =>
        rawInstallations
            .GroupBy(static sdk => $"{sdk.Version.Raw}|{sdk.SdkRoot}", comparer)
            .Select(static group => group.First())
            .OrderByDescending(static sdk => sdk.Version)
            .ThenByDescending(static sdk => sdk.IsManaged)
            .ThenBy(sdk => Architectures.MatchesHost(sdk.Architecture, _environment.RuntimeArchitecture) ? 0 : 1)
            .ThenBy(static sdk => sdk.Version.Raw, StringComparer.Ordinal)
            .ToList();

    private static string? ParseManagedArchitecture(string hostRoot)
    {
        var name = Path.GetFileName(hostRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        // The architecture suffix is always appended LAST ("<version>--<arch>"),
        // while the version itself may contain "--" in its prerelease label.
        var separator = name.LastIndexOf("--", StringComparison.Ordinal);
        if (separator < 0)
        {
            return null;
        }

        var candidate = name[(separator + 2)..];

        // A present-but-unknown suffix must NOT read as native ("unknown" never
        // matches the host), otherwise a foreign-arch install could be
        // auto-selected or satisfy a native install request. The version part
        // must parse too, or "--" belongs to the version label itself.
        var versionPart = name[..separator];
        if (!SdkVersion.TryParse(versionPart, out _))
        {
            return null;
        }

        return Architectures.IsKnown(candidate) ? candidate.ToLowerInvariant() : "unknown";
    }

    public string? GetSystemDotnetPath()
    {
        foreach (var root in _environment.GetKnownDotnetRoots())
        {
            var dotnetPath = ResolveDotnetPath(root);
            if (dotnetPath is not null)
            {
                return dotnetPath;
            }
        }

        return null;
    }

    private string? ResolveDotnetPath(string hostRoot)
    {
        var fileName = _environment.IsWindows ? "dotnet.exe" : "dotnet";
        var fullPath = Path.Combine(hostRoot, fileName);
        return File.Exists(fullPath) ? fullPath : null;
    }

    private static string BuildOriginHint(SdkOriginKind origin, string hostRoot, string source) =>
        origin switch
        {
            SdkOriginKind.ManagedInstall => $"Installed and isolated by dotver at {hostRoot}",
            SdkOriginKind.OfficialInstallRoot => $"Official system root detected at {hostRoot}",
            SdkOriginKind.RegisteredRoot => $"Manually registered root in config ({hostRoot})",
            SdkOriginKind.PathProbe => $"Root detected from PATH ({hostRoot})",
            SdkOriginKind.CustomPath => $"Root detected via DOTNET_ROOT variable ({hostRoot})",
            _ => $"Discovered from {source} at {hostRoot}"
        };
}
