using System.Text.Json;
using DotVer.Domain;

namespace DotVer.Application;

public sealed class GlobalJsonService
{
    public GlobalJsonRequest? TryFind(string workingDirectory)
    {
        var cursor = new DirectoryInfo(workingDirectory);
        while (cursor is not null)
        {
            var candidate = Path.Combine(cursor.FullName, "global.json");
            if (File.Exists(candidate))
            {
                using var stream = File.OpenRead(candidate);
                using var document = JsonDocument.Parse(stream);
                if (!document.RootElement.TryGetProperty("sdk", out var sdkNode))
                {
                    return null;
                }

                if (!sdkNode.TryGetProperty("version", out var versionNode) ||
                    !SdkVersion.TryParse(versionNode.GetString(), out var version))
                {
                    return null;
                }

                var rollForward = sdkNode.TryGetProperty("rollForward", out var rollForwardNode)
                    ? ParseRollForward(rollForwardNode.GetString())
                    : SdkRollForward.Default;

                return new GlobalJsonRequest(version!, rollForward);
            }

            cursor = cursor.Parent;
        }

        return null;
    }

    private static SdkRollForward ParseRollForward(string? raw) =>
        raw?.Trim().ToLowerInvariant() switch
        {
            "disable" => SdkRollForward.Disable,
            "patch" => SdkRollForward.Patch,
            "feature" => SdkRollForward.Feature,
            "minor" => SdkRollForward.Minor,
            "major" => SdkRollForward.Major,
            "latestpatch" => SdkRollForward.LatestPatch,
            "latestfeature" => SdkRollForward.LatestFeature,
            "latestminor" => SdkRollForward.LatestMinor,
            "latestmajor" => SdkRollForward.LatestMajor,
            _ => SdkRollForward.Default
        };
}

public sealed class VersionResolver
{
    public string ResolveToken(string token, DotVerConfig config, IReadOnlyList<SdkInstallation> installations)
    {
        if (string.Equals(token, "current", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(config.CurrentVersion))
        {
            return config.CurrentVersion;
        }

        if (string.Equals(token, "default", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(config.DefaultVersion))
        {
            return config.DefaultVersion;
        }

        if (string.Equals(token, "latest", StringComparison.OrdinalIgnoreCase))
        {
            return installations
                .OrderByDescending(static sdk => sdk.Version)
                .FirstOrDefault()
                ?.Version.Raw
                ?? token;
        }

        if (config.Aliases.TryGetValue(token, out var alias))
        {
            return alias;
        }

        return token;
    }

    public SdkInstallation? FindByToken(string token, DotVerConfig config, IReadOnlyList<SdkInstallation> installations)
    {
        var resolved = ResolveToken(token, config, installations);
        var ordered = installations.OrderByDescending(static sdk => sdk.Version);

        return ordered.FirstOrDefault(sdk => string.Equals(sdk.Version.Raw, resolved, StringComparison.OrdinalIgnoreCase))
            ?? ordered.FirstOrDefault(sdk => sdk.Version.Raw.StartsWith(resolved + ".", StringComparison.OrdinalIgnoreCase)
                                          || sdk.Version.Raw.StartsWith(resolved, StringComparison.OrdinalIgnoreCase)
                                             && resolved.Contains('.'));
    }
}

public sealed class InventoryService
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
            SdkOriginKind.ManagedInstall => $"Instalado y aislado por dotver en {hostRoot}",
            SdkOriginKind.OfficialInstallRoot => $"Raiz oficial del sistema detectada en {hostRoot}",
            SdkOriginKind.RegisteredRoot => $"Raiz registrada manualmente en config ({hostRoot})",
            SdkOriginKind.PathProbe => $"Raiz detectada desde PATH ({hostRoot})",
            SdkOriginKind.CustomPath => $"Raiz detectada por variable DOTNET_ROOT ({hostRoot})",
            _ => $"Descubierto desde {source} en {hostRoot}"
        };
}

public sealed class SelectionService
{
    private readonly InventoryService _inventoryService;
    private readonly IConfigStore _configStore;
    private readonly GlobalJsonService _globalJsonService;
    private readonly VersionResolver _versionResolver;
    private readonly IEnvironmentContext _environment;

    public SelectionService(
        InventoryService inventoryService,
        IConfigStore configStore,
        GlobalJsonService globalJsonService,
        VersionResolver versionResolver,
        IEnvironmentContext environment)
    {
        _inventoryService = inventoryService;
        _configStore = configStore;
        _globalJsonService = globalJsonService;
        _versionResolver = versionResolver;
        _environment = environment;
    }

    public async Task<SdkSelection> ResolveAsync(CancellationToken cancellationToken)
    {
        var installations = await _inventoryService.ScanAsync(cancellationToken).ConfigureAwait(false);
        var config = await _configStore.LoadAsync(cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(config.CurrentVersion))
        {
            var current = _versionResolver.FindByToken(config.CurrentVersion, config, installations);
            if (current is not null)
            {
                return new SdkSelection(current, $"Seleccionado por 'use' persistido ({config.CurrentVersion})", false);
            }
        }

        if (!string.IsNullOrWhiteSpace(config.DefaultVersion))
        {
            var fallback = _versionResolver.FindByToken(config.DefaultVersion, config, installations);
            if (fallback is not null)
            {
                return new SdkSelection(fallback, $"Seleccionado por default ({config.DefaultVersion})", false);
            }
        }

        var globalJson = _globalJsonService.TryFind(_environment.CurrentDirectory);
        if (globalJson is not null)
        {
            var selection = MatchGlobalJson(globalJson, installations);
            if (selection is not null)
            {
                return new SdkSelection(selection, $"Seleccionado por global.json ({globalJson.Version.Raw})", true);
            }
        }

        var systemPath = _inventoryService.GetSystemDotnetPath();
        var systemRootSelection = installations.FirstOrDefault(sdk =>
            string.Equals(sdk.DotnetPath, systemPath, _environment.IsWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));

        return new SdkSelection(systemRootSelection, "Seleccionado por fallback del sistema", false);
    }

    private static SdkInstallation? MatchGlobalJson(GlobalJsonRequest request, IReadOnlyList<SdkInstallation> installations)
    {
        var ordered = installations.OrderByDescending(static sdk => sdk.Version).ToList();
        var exact = ordered.FirstOrDefault(sdk => sdk.Version.CompareTo(request.Version) == 0);
        if (request.RollForward is SdkRollForward.Default or SdkRollForward.Disable)
        {
            return exact;
        }

        if (exact is not null)
        {
            return exact;
        }

        return request.RollForward switch
        {
            SdkRollForward.Patch or SdkRollForward.LatestPatch => ordered.FirstOrDefault(sdk =>
                sdk.Version.Major == request.Version.Major &&
                sdk.Version.Minor == request.Version.Minor &&
                sdk.Version.FeatureBand == request.Version.FeatureBand),
            SdkRollForward.Feature or SdkRollForward.LatestFeature => ordered.FirstOrDefault(sdk =>
                sdk.Version.Major == request.Version.Major &&
                sdk.Version.Minor == request.Version.Minor &&
                sdk.Version.FeatureBand >= request.Version.FeatureBand),
            SdkRollForward.Minor or SdkRollForward.LatestMinor => ordered.FirstOrDefault(sdk =>
                sdk.Version.Major == request.Version.Major &&
                sdk.Version.Minor >= request.Version.Minor),
            SdkRollForward.Major or SdkRollForward.LatestMajor => ordered.FirstOrDefault(sdk =>
                sdk.Version.Major >= request.Version.Major),
            _ => exact
        };
    }
}

public sealed class InterceptorService
{
    private readonly SelectionService _selectionService;
    private readonly InventoryService _inventoryService;
    private readonly IProcessRunner _processRunner;

    public InterceptorService(SelectionService selectionService, InventoryService inventoryService, IProcessRunner processRunner)
    {
        _selectionService = selectionService;
        _inventoryService = inventoryService;
        _processRunner = processRunner;
    }

    public async Task<ProcessResult> ExecuteAsync(IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        var selection = await _selectionService.ResolveAsync(cancellationToken).ConfigureAwait(false);
        var installations = await _inventoryService.ScanAsync(cancellationToken).ConfigureAwait(false);
        var dotnetPath = selection.Installation?.DotnetPath ?? _inventoryService.GetSystemDotnetPath() ?? "dotnet";

        if (args.SequenceEqual(["--version"]))
        {
            var value = selection.Installation?.Version.Raw
                ?? installations.OrderByDescending(static sdk => sdk.Version).FirstOrDefault()?.Version.Raw
                ?? "unknown";

            return new ProcessResult(0, value + Environment.NewLine, string.Empty);
        }

        if (args.SequenceEqual(["--list-sdks"]))
        {
            var output = string.Join(
                Environment.NewLine,
                installations
                    .OrderByDescending(static sdk => sdk.Version)
                    .Select(static sdk => $"{sdk.Version.Raw} [{sdk.SdkRoot}]"));

            return new ProcessResult(0, output + Environment.NewLine, string.Empty);
        }

        if (args.SequenceEqual(["--info"]))
        {
            var info = await _processRunner.RunAsync(new ProcessSpec(dotnetPath, args), cancellationToken).ConfigureAwait(false);
            var extra = Environment.NewLine +
                        "DotVer:" + Environment.NewLine +
                        $"  Intercepted: yes{Environment.NewLine}" +
                        $"  Reason: {selection.Reason}{Environment.NewLine}" +
                        $"  Selected SDK: {selection.Installation?.Version.Raw ?? "system-default"}{Environment.NewLine}" +
                        $"  Selected Root: {selection.Installation?.HostRoot ?? "n/a"}{Environment.NewLine}";

            return info with { StandardOutput = info.StandardOutput + extra };
        }

        return await _processRunner.RunAsync(new ProcessSpec(dotnetPath, args), cancellationToken).ConfigureAwait(false);
    }
}

public sealed class ShimService
{
    private readonly DotVerPaths _paths;
    private readonly IEnvironmentContext _environment;

    public ShimService(DotVerPaths paths, IEnvironmentContext environment)
    {
        _paths = paths;
        _environment = environment;
    }

    public Task<ShimInstallResult> InstallAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Directory.CreateDirectory(_paths.Shims);
        Directory.CreateDirectory(_paths.Env);

        var files = new List<string>();
        var launcher = BuildLauncher();

        if (_environment.IsWindows)
        {
            files.Add(WriteFile("dotnet.cmd", $"@echo off{Environment.NewLine}{launcher.WindowsInterceptCommand}%*{Environment.NewLine}"));
            files.Add(WriteFile("dotver.ps1", $"{launcher.PowerShellInvoke} @args{Environment.NewLine}"));

            var psEnv = $"$env:PATH = \"{_paths.Shims};$env:PATH\"{Environment.NewLine}" +
                        $"function global:dotnet {{ {launcher.PowerShellInterceptInvoke} @args }}{Environment.NewLine}";

            files.Add(WriteEnvFile("dotver.ps1", psEnv));
            files.Add(WriteEnvFile("dotver.cmd", $"@echo off{Environment.NewLine}set PATH={_paths.Shims};%PATH%{Environment.NewLine}"));
        }
        else
        {
            files.Add(WriteExecutableFile("dotnet", $"#!/usr/bin/env bash{Environment.NewLine}{launcher.ShellInterceptCommand}\"$@\"{Environment.NewLine}"));
            files.Add(WriteEnvFile("dotver.sh", $"export PATH=\"{_paths.Shims}:$PATH\"{Environment.NewLine}"));
            files.Add(WriteEnvFile("dotver.fish", $"set -gx PATH \"{_paths.Shims}\" $PATH{Environment.NewLine}"));
        }

        return Task.FromResult(new ShimInstallResult(_paths.Shims, files));
    }

    public string RenderEnvSnippet(ShellKind shellKind) =>
        shellKind switch
        {
            ShellKind.PowerShell => $". \"{Path.Combine(_paths.Env, "dotver.ps1")}\"",
            ShellKind.Cmd => $"call \"{Path.Combine(_paths.Env, "dotver.cmd")}\"",
            ShellKind.Fish => $"source \"{Path.Combine(_paths.Env, "dotver.fish")}\"",
            _ => $"source \"{Path.Combine(_paths.Env, "dotver.sh")}\""
        };

    private string WriteFile(string name, string content)
    {
        var path = Path.Combine(_paths.Shims, name);
        WriteTextIfChanged(path, content);
        return path;
    }

    private string WriteEnvFile(string name, string content)
    {
        var path = Path.Combine(_paths.Env, name);
        WriteTextIfChanged(path, content);
        return path;
    }

    private string WriteExecutableFile(string name, string content)
    {
        var path = WriteFile(name, content);
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                                       UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                                       UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }

        return path;
    }

    private static void WriteTextIfChanged(string path, string content)
    {
        if (File.Exists(path))
        {
            var existing = File.ReadAllText(path);
            if (string.Equals(existing, content, StringComparison.Ordinal))
            {
                return;
            }
        }

        File.WriteAllText(path, content);
    }

    private ShimLauncher BuildLauncher()
    {
        var processPath = _environment.ProcessPath;
        var entryAssemblyPath = _environment.EntryAssemblyPath;

        var runsAsManagedDll = Path.GetExtension(entryAssemblyPath).Equals(".dll", StringComparison.OrdinalIgnoreCase) &&
                               Path.GetFileName(processPath).StartsWith("dotnet", StringComparison.OrdinalIgnoreCase);

        if (runsAsManagedDll)
        {
            return new ShimLauncher(
                $"\"{processPath}\" \"{entryAssemblyPath}\" ",
                $"& \"{processPath}\" \"{entryAssemblyPath}\"",
                $"\"{processPath}\" \"{entryAssemblyPath}\" intercept -- ",
                $"& \"{processPath}\" \"{entryAssemblyPath}\" intercept --");
        }

        return new ShimLauncher(
            $"\"{processPath}\" ",
            $"& \"{processPath}\"",
            $"\"{processPath}\" intercept -- ",
            $"& \"{processPath}\" intercept --");
    }

    private sealed record ShimLauncher(string ShellInvokePrefix, string PowerShellInvoke, string ShellInterceptCommand, string PowerShellInterceptInvoke)
    {
        public string WindowsInterceptCommand => ShellInterceptCommand;
    }
}

public sealed class EnvironmentBootstrapService
{
    private const string MarkerStart = "# >>> dotver >>>";
    private const string MarkerEnd = "# <<< dotver <<<";

    private readonly DotVerPaths _paths;
    private readonly IEnvironmentContext _environment;
    private readonly ShimService _shimService;

    public EnvironmentBootstrapService(DotVerPaths paths, IEnvironmentContext environment, ShimService shimService)
    {
        _paths = paths;
        _environment = environment;
        _shimService = shimService;
    }

    public async Task<EnvironmentBootstrapResult> EnsureConfiguredAsync(CancellationToken cancellationToken)
    {
        var shimResult = await _shimService.InstallAsync(cancellationToken).ConfigureAwait(false);
        var profilesUpdated = new List<string>();
        var notes = new List<string>();

        var processPathUpdated = EnsureProcessPath();
        var persistentPathUpdated = _environment.IsWindows
            ? EnsureWindowsIntegration(profilesUpdated, notes)
            : EnsureUnixProfiles(profilesUpdated);

        EnsureDotVerHome();

        if (persistentPathUpdated)
        {
            notes.Add("La configuracion persistente del entorno fue actualizada.");
        }

        notes.Add(_environment.IsWindows
            ? "En Windows, dotver actualiza PATH y perfiles de PowerShell; abre una nueva terminal para que el shim tenga prioridad sobre el dotnet del sistema."
            : "Abre una nueva terminal si tu shell actual no recarga automaticamente el perfil.");

        return new EnvironmentBootstrapResult(_paths.Shims, persistentPathUpdated, processPathUpdated, profilesUpdated, notes);
    }

    private bool EnsureProcessPath()
    {
        var currentPath = _environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var entries = currentPath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (entries.Contains(_paths.Shims, _environment.IsWindows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal))
        {
            return false;
        }

        var updated = string.IsNullOrWhiteSpace(currentPath)
            ? _paths.Shims
            : _paths.Shims + Path.PathSeparator + currentPath;

        Environment.SetEnvironmentVariable("PATH", updated, EnvironmentVariableTarget.Process);
        return true;
    }

    private bool EnsureWindowsIntegration(List<string> profilesUpdated, List<string> notes)
    {
        var pathChanged = EnsureWindowsUserPath(notes);
        var profilesChanged = EnsureWindowsPowerShellProfiles(profilesUpdated);
        return pathChanged || profilesChanged;
    }

    private bool EnsureWindowsUserPath(List<string> notes)
    {
        var userPath = _environment.GetUserEnvironmentVariable("PATH") ?? string.Empty;
        var entries = userPath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (entries.Contains(_paths.Shims, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        var updated = string.IsNullOrWhiteSpace(userPath)
            ? _paths.Shims
            : _paths.Shims + Path.PathSeparator + userPath;

        _environment.SetUserEnvironmentVariable("PATH", updated);
        notes.Add($"Se agrego {_paths.Shims} al PATH de usuario.");
        return true;
    }

    private bool EnsureWindowsPowerShellProfiles(List<string> profilesUpdated)
    {
        var profiles = new[]
        {
            Path.Combine(_environment.DocumentsDirectory, "PowerShell", "Microsoft.PowerShell_profile.ps1"),
            Path.Combine(_environment.DocumentsDirectory, "WindowsPowerShell", "Microsoft.PowerShell_profile.ps1")
        };

        var changed = false;
        var snippet = $". \"{Path.Combine(_paths.Env, "dotver.ps1")}\"";
        foreach (var profile in profiles)
        {
            if (EnsureProfileContains(profile, snippet))
            {
                profilesUpdated.Add(profile);
                changed = true;
            }
        }

        return changed;
    }

    private bool EnsureUnixProfiles(List<string> profilesUpdated)
    {
        var targets = new[]
        {
            Path.Combine(_environment.HomeDirectory, ".profile"),
            Path.Combine(_environment.HomeDirectory, ".bashrc"),
            Path.Combine(_environment.HomeDirectory, ".zshrc"),
            Path.Combine(_environment.HomeDirectory, ".config", "fish", "config.fish")
        };

        var changed = false;
        foreach (var profile in targets)
        {
            var sourceSnippet = profile.EndsWith("config.fish", StringComparison.OrdinalIgnoreCase)
                ? $"source \"{Path.Combine(_paths.Env, "dotver.fish")}\""
                : $"source \"{Path.Combine(_paths.Env, "dotver.sh")}\"";
            if (EnsureProfileContains(profile, sourceSnippet))
            {
                profilesUpdated.Add(profile);
                changed = true;
            }
        }

        return changed;
    }

    private static bool EnsureProfileContains(string profile, string sourceSnippet)
    {
        var block = $"{MarkerStart}{Environment.NewLine}{sourceSnippet}{Environment.NewLine}{MarkerEnd}{Environment.NewLine}";
        Directory.CreateDirectory(Path.GetDirectoryName(profile)!);
        string existing;
        try
        {
            existing = File.Exists(profile) ? File.ReadAllText(profile) : string.Empty;
        }
        catch (IOException)
        {
            return false;
        }

        var startIdx = existing.IndexOf(MarkerStart, StringComparison.Ordinal);
        if (startIdx >= 0)
        {
            var endIdx = existing.IndexOf(MarkerEnd, startIdx, StringComparison.Ordinal);
            if (endIdx >= 0)
            {
                var endOfBlock = endIdx + MarkerEnd.Length;
                if (endOfBlock < existing.Length && existing[endOfBlock] == '\r') endOfBlock++;
                if (endOfBlock < existing.Length && existing[endOfBlock] == '\n') endOfBlock++;

                var currentBlock = existing[startIdx..endOfBlock];
                if (string.Equals(currentBlock, block, StringComparison.Ordinal))
                {
                    return false;
                }

                var updated = existing[..startIdx] + block + existing[endOfBlock..];
                File.WriteAllText(profile, updated);
                return true;
            }
        }

        File.AppendAllText(profile, (existing.EndsWith(Environment.NewLine, StringComparison.Ordinal) || existing.Length == 0 ? string.Empty : Environment.NewLine) + block);
        return true;
    }

    private void EnsureDotVerHome()
    {
        Environment.SetEnvironmentVariable("DOTVER_HOME", _paths.Home, EnvironmentVariableTarget.Process);
        if (_environment.IsWindows)
        {
            _environment.SetUserEnvironmentVariable("DOTVER_HOME", _paths.Home);
        }
    }
}

public sealed class InstallService
{
    private const string PowerShellInstallScript = "https://dot.net/v1/dotnet-install.ps1";
    private const string ShellInstallScript = "https://dot.net/v1/dotnet-install.sh";
    private const string ReleasesIndexUrl = "https://builds.dotnet.microsoft.com/dotnet/release-metadata/releases-index.json";

    private readonly DotVerPaths _paths;
    private readonly IEnvironmentContext _environment;
    private readonly IHttpResourceClient _httpResourceClient;
    private readonly IProcessRunner _processRunner;
    private readonly InventoryService _inventoryService;

    public InstallService(
        DotVerPaths paths,
        IEnvironmentContext environment,
        IHttpResourceClient httpResourceClient,
        IProcessRunner processRunner,
        InventoryService inventoryService)
    {
        _paths = paths;
        _environment = environment;
        _httpResourceClient = httpResourceClient;
        _processRunner = processRunner;
        _inventoryService = inventoryService;
    }

    public async Task<InstallPlan> ResolveInstallPlanAsync(string token, CancellationToken cancellationToken)
    {
        if (SdkVersion.TryParse(token, out var exactVersion))
        {
            return new InstallPlan(token, exactVersion!.Raw, "Version exacta solicitada por el usuario.", null);
        }

        var channels = await ListRemoteAsync(cancellationToken).ConfigureAwait(false);
        var normalized = token.Trim().ToLowerInvariant();

        ReleaseChannel? selectedChannel = normalized switch
        {
            "latest" => channels
                .Where(static channel => !string.Equals(channel.SupportPhase, "preview", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(ParseChannelVersion)
                .FirstOrDefault(),
            "lts" => channels
                .Where(channel => string.Equals(channel.ReleaseType, "lts", StringComparison.OrdinalIgnoreCase) &&
                                  string.Equals(channel.SupportPhase, "active", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(ParseChannelVersion)
                .FirstOrDefault(),
            "sts" => channels
                .Where(channel => string.Equals(channel.ReleaseType, "sts", StringComparison.OrdinalIgnoreCase) &&
                                  string.Equals(channel.SupportPhase, "active", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(ParseChannelVersion)
                .FirstOrDefault(),
            _ => channels.FirstOrDefault(channel =>
                string.Equals(channel.Channel, normalized, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(channel.Channel, NormalizeChannelToken(normalized), StringComparison.OrdinalIgnoreCase))
        };

        if (selectedChannel is null)
        {
            throw new InvalidOperationException($"No se pudo resolver '{token}' como version ni como canal.");
        }

        return new InstallPlan(token, selectedChannel.LatestSdkVersion, $"Canal {selectedChannel.Channel} resuelto a {selectedChannel.LatestSdkVersion}.", selectedChannel);
    }

    public async Task<InstallOutcome> InstallAsync(string versionOrChannel, CancellationToken cancellationToken)
    {
        var plan = await ResolveInstallPlanAsync(versionOrChannel, cancellationToken).ConfigureAwait(false);
        var alreadyInstalled = (await _inventoryService.ScanAsync(cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(sdk => sdk.IsManaged && string.Equals(sdk.Version.Raw, plan.ResolvedVersion, StringComparison.OrdinalIgnoreCase));

        if (alreadyInstalled is not null)
        {
            return new InstallOutcome(plan, alreadyInstalled, true);
        }

        Directory.CreateDirectory(_paths.ManagedInstalls);
        Directory.CreateDirectory(_paths.Scripts);

        var installRoot = Path.Combine(_paths.ManagedInstalls, plan.ResolvedVersion);
        Directory.CreateDirectory(installRoot);

        if (_environment.IsWindows)
        {
            var scriptPath = Path.Combine(_paths.Scripts, "dotnet-install.ps1");
            await _httpResourceClient.DownloadFileAsync(PowerShellInstallScript, scriptPath, cancellationToken).ConfigureAwait(false);

            var result = await _processRunner.RunAsync(
                new ProcessSpec(
                    "pwsh",
                    ["-NoProfile", "-File", scriptPath, "-Version", plan.ResolvedVersion, "-InstallDir", installRoot, "-NoPath"]),
                cancellationToken).ConfigureAwait(false);

            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(result.StandardError);
            }
        }
        else
        {
            var scriptPath = Path.Combine(_paths.Scripts, "dotnet-install.sh");
            await _httpResourceClient.DownloadFileAsync(ShellInstallScript, scriptPath, cancellationToken).ConfigureAwait(false);
            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                File.SetUnixFileMode(scriptPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            var result = await _processRunner.RunAsync(
                new ProcessSpec(
                    "/bin/bash",
                    [scriptPath, "--version", plan.ResolvedVersion, "--install-dir", installRoot, "--no-path"]),
                cancellationToken).ConfigureAwait(false);

            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(result.StandardError);
            }
        }

        var installed = (await _inventoryService.ScanAsync(cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(sdk => sdk.IsManaged && string.Equals(sdk.Version.Raw, plan.ResolvedVersion, StringComparison.OrdinalIgnoreCase));

        return new InstallOutcome(
            plan,
            installed ?? throw new InvalidOperationException($"No se pudo validar la instalacion de {plan.ResolvedVersion}."),
            false);
    }

    public async Task<IReadOnlyList<ReleaseChannel>> ListRemoteAsync(CancellationToken cancellationToken)
    {
        var payload = await _httpResourceClient.GetStringAsync(ReleasesIndexUrl, cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(payload);

        return document.RootElement
            .GetProperty("releases-index")
            .EnumerateArray()
            .Select(node => new ReleaseChannel(
                node.GetProperty("channel-version").GetString() ?? "unknown",
                node.GetProperty("latest-sdk").GetString() ?? "unknown",
                node.TryGetProperty("support-phase", out var supportPhase) ? supportPhase.GetString() ?? "unknown" : "unknown",
                node.TryGetProperty("release-type", out var releaseType) ? releaseType.GetString() ?? "unknown" : "unknown"))
            .OrderByDescending(static channel => channel.Channel, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static Version ParseChannelVersion(ReleaseChannel channel)
    {
        var value = channel.Channel.Contains('.', StringComparison.Ordinal) ? channel.Channel : channel.Channel + ".0";
        return Version.TryParse(value, out var version) ? version : new Version(0, 0);
    }

    private static string NormalizeChannelToken(string token) =>
        token.EndsWith(".0", StringComparison.OrdinalIgnoreCase)
            ? token
            : token.Contains('.', StringComparison.Ordinal) ? token : token + ".0";
}

public sealed class StateService
{
    private readonly IConfigStore _configStore;
    private readonly VersionResolver _versionResolver;
    private readonly InventoryService _inventoryService;
    private readonly DotVerPaths _paths;

    public StateService(IConfigStore configStore, VersionResolver versionResolver, InventoryService inventoryService, DotVerPaths paths)
    {
        _configStore = configStore;
        _versionResolver = versionResolver;
        _inventoryService = inventoryService;
        _paths = paths;
    }

    public async Task<DotVerConfig> GetConfigAsync(CancellationToken cancellationToken) =>
        await _configStore.LoadAsync(cancellationToken).ConfigureAwait(false);

    public async Task<SdkInstallation> SetCurrentAsync(string token, CancellationToken cancellationToken)
    {
        var config = await _configStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        var installations = await _inventoryService.ScanAsync(cancellationToken).ConfigureAwait(false);
        var sdk = _versionResolver.FindByToken(token, config, installations)
            ?? throw new InvalidOperationException($"No se encontro una version selectable para '{token}'.");

        config = config with { CurrentVersion = sdk.Version.Raw };
        await _configStore.SaveAsync(config, cancellationToken).ConfigureAwait(false);
        return sdk;
    }

    public async Task<SdkInstallation> SetCurrentAndDefaultAsync(string token, CancellationToken cancellationToken)
    {
        var config = await _configStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        var installations = await _inventoryService.ScanAsync(cancellationToken).ConfigureAwait(false);
        var sdk = _versionResolver.FindByToken(token, config, installations)
            ?? throw new InvalidOperationException($"No se encontro una version selectable para '{token}'.");

        config = config with { CurrentVersion = sdk.Version.Raw, DefaultVersion = sdk.Version.Raw };
        await _configStore.SaveAsync(config, cancellationToken).ConfigureAwait(false);
        return sdk;
    }

    public async Task<SdkInstallation> SetDefaultAsync(string token, CancellationToken cancellationToken)
    {
        var config = await _configStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        var installations = await _inventoryService.ScanAsync(cancellationToken).ConfigureAwait(false);
        var sdk = _versionResolver.FindByToken(token, config, installations)
            ?? throw new InvalidOperationException($"No se encontro una version selectable para '{token}'.");

        config = config with { DefaultVersion = sdk.Version.Raw };
        await _configStore.SaveAsync(config, cancellationToken).ConfigureAwait(false);
        return sdk;
    }

    public async Task AddAliasAsync(string alias, string versionToken, CancellationToken cancellationToken)
    {
        var config = await _configStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        var installations = await _inventoryService.ScanAsync(cancellationToken).ConfigureAwait(false);
        var sdk = _versionResolver.FindByToken(versionToken, config, installations)
            ?? throw new InvalidOperationException($"No se encontro una version selectable para '{versionToken}'.");

        config.Aliases[alias] = sdk.Version.Raw;
        await _configStore.SaveAsync(config, cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveAliasAsync(string alias, CancellationToken cancellationToken)
    {
        var config = await _configStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        config.Aliases.Remove(alias);
        await _configStore.SaveAsync(config, cancellationToken).ConfigureAwait(false);
    }

    public async Task RegisterRootAsync(string path, CancellationToken cancellationToken)
    {
        var config = await _configStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (!config.RegisteredRoots.Contains(path, StringComparer.OrdinalIgnoreCase))
        {
            config.RegisteredRoots.Add(path);
        }

        await _configStore.SaveAsync(config, cancellationToken).ConfigureAwait(false);
    }

    public async Task<UninstallOutcome> UninstallManagedAsync(string token, CancellationToken cancellationToken)
    {
        var config = await _configStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        var inventory = await _inventoryService.ScanAsync(cancellationToken).ConfigureAwait(false);
        var target = _versionResolver.FindByToken(token, config, inventory)
                     ?? inventory.FirstOrDefault(sdk =>
                         sdk.IsManaged &&
                         string.Equals(sdk.Version.Raw, token, StringComparison.OrdinalIgnoreCase));

        if (target is null || !target.IsManaged)
        {
            throw new InvalidOperationException($"Solo se pueden desinstalar SDKs managed por dotver. Token recibido: '{token}'.");
        }

        var removedAliases = config.Aliases
            .Where(pair => string.Equals(pair.Value, target.Version.Raw, StringComparison.OrdinalIgnoreCase))
            .Select(static pair => pair.Key)
            .ToList();

        foreach (var alias in removedAliases)
        {
            config.Aliases.Remove(alias);
        }

        var removedDirectory = Directory.Exists(target.HostRoot);
        if (removedDirectory)
        {
            Directory.Delete(target.HostRoot, recursive: true);
        }

        var remainingInventory = (await _inventoryService.ScanAsync(cancellationToken).ConfigureAwait(false))
            .Where(static sdk => sdk.IsManaged)
            .OrderByDescending(static sdk => sdk.Version)
            .ToList();

        var fallback = remainingInventory.FirstOrDefault();
        var currentVersion = string.Equals(config.CurrentVersion, target.Version.Raw, StringComparison.OrdinalIgnoreCase)
            ? fallback?.Version.Raw
            : config.CurrentVersion;
        var defaultVersion = string.Equals(config.DefaultVersion, target.Version.Raw, StringComparison.OrdinalIgnoreCase)
            ? fallback?.Version.Raw
            : config.DefaultVersion;

        config = config with
        {
            CurrentVersion = currentVersion,
            DefaultVersion = defaultVersion
        };

        await _configStore.SaveAsync(config, cancellationToken).ConfigureAwait(false);

        if (!Directory.Exists(_paths.ManagedInstalls))
        {
            Directory.CreateDirectory(_paths.ManagedInstalls);
        }

        return new UninstallOutcome(target.Version.Raw, removedDirectory, currentVersion, defaultVersion, removedAliases);
    }
}
