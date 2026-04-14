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

    public async Task<IReadOnlyList<SdkInstallation>> ScanAsync(CancellationToken cancellationToken)
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

        foreach (var knownRoot in GetKnownRoots())
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

        var rawInstallations = new List<SdkInstallation>();
        foreach (var entry in rootHints)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var hostRoot = entry.Key;
            var dotnetPath = ResolveDotnetPath(hostRoot);
            if (dotnetPath is null)
            {
                continue;
            }

            var sdkDirectory = Path.Combine(hostRoot, "sdk");
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

            var directlySelectable = entry.Value.IsManaged || versions.Count == 1;
            rawInstallations.AddRange(
                versions.Select(version => new SdkInstallation(
                    version,
                    hostRoot,
                    Path.Combine(sdkDirectory, version.Raw),
                    dotnetPath,
                    entry.Value.Origin,
                    BuildOriginHint(entry.Value.Origin, hostRoot, entry.Value.Source),
                    entry.Value.IsManaged,
                    directlySelectable,
                    entry.Value.Source)));
        }

        return rawInstallations
            .GroupBy(static sdk => $"{sdk.Version.Raw}|{sdk.SdkRoot}", comparer)
            .Select(static group => group.First())
            .OrderByDescending(static sdk => sdk.Version)
            .ToList();
    }

    public string? GetSystemDotnetPath()
    {
        foreach (var root in GetKnownRoots())
        {
            var dotnetPath = ResolveDotnetPath(root);
            if (dotnetPath is not null)
            {
                return dotnetPath;
            }
        }

        return null;
    }

    private IEnumerable<string> GetKnownRoots()
    {
        if (_environment.IsWindows)
        {
            var programFiles = _environment.GetEnvironmentVariable("ProgramFiles");
            var programFilesX86 = _environment.GetEnvironmentVariable("ProgramFiles(x86)");

            if (!string.IsNullOrWhiteSpace(programFiles))
            {
                yield return Path.Combine(programFiles, "dotnet");
            }

            if (!string.IsNullOrWhiteSpace(programFilesX86))
            {
                yield return Path.Combine(programFilesX86, "dotnet");
            }

            yield break;
        }

        yield return "/usr/share/dotnet";
        yield return "/usr/local/share/dotnet";
        yield return "/usr/lib/dotnet";
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
