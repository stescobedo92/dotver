using DotVer.Domain;

namespace DotVer.Application;

public sealed class DoctorService : IDoctorService
{
    private readonly IInventoryService _inventoryService;
    private readonly IConfigStore _configStore;
    private readonly IGlobalJsonService _globalJsonService;
    private readonly IEnvironmentBootstrapService _bootstrapService;
    private readonly DotVerPaths _paths;
    private readonly IEnvironmentContext _environment;

    public DoctorService(
        IInventoryService inventoryService,
        IConfigStore configStore,
        IGlobalJsonService globalJsonService,
        IEnvironmentBootstrapService bootstrapService,
        DotVerPaths paths,
        IEnvironmentContext environment)
    {
        _inventoryService = inventoryService;
        _configStore = configStore;
        _globalJsonService = globalJsonService;
        _bootstrapService = bootstrapService;
        _paths = paths;
        _environment = environment;
    }

    public async Task<DoctorResult> DiagnoseAsync(CancellationToken cancellationToken)
    {
        var inventory = await _inventoryService.ScanAsync(cancellationToken).ConfigureAwait(false);
        var config = await _configStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        return new DoctorResult(Diagnose(inventory, config), []);
    }

    public async Task<DoctorResult> FixAsync(CancellationToken cancellationToken)
    {
        var fixes = new List<string>();

        var shimFile = Path.Combine(_paths.Shims, _environment.IsWindows ? "dotnet.cmd" : "dotnet");
        var shimsWereMissing = !File.Exists(shimFile);
        var bootstrap = await _bootstrapService.EnsureConfiguredAsync(cancellationToken).ConfigureAwait(false);
        if (shimsWereMissing || bootstrap.PersistentPathUpdated || bootstrap.ProcessPathUpdated || bootstrap.ProfilesUpdated.Count > 0)
        {
            fixes.Add($"Environment reconfigured (shims: {TerminalText.Sanitize(bootstrap.ShimsDirectory)}).");
        }

        var inventory = await _inventoryService.ScanAsync(cancellationToken).ConfigureAwait(false);
        var config = await _configStore.LoadAsync(cancellationToken).ConfigureAwait(false);

        // Never destroy config based on negative evidence from a possibly
        // degraded scan (disconnected network/USB root, install in progress):
        // missing roots or an empty inventory abort the config repair.
        var hasMissingRoots = config.RegisteredRoots.Any(static root => !Directory.Exists(root));
        var referencesVersions = !string.IsNullOrWhiteSpace(config.CurrentVersion) ||
                                 !string.IsNullOrWhiteSpace(config.DefaultVersion) ||
                                 config.Aliases.Count > 0;
        var repairSkipped = hasMissingRoots || (inventory.Count == 0 && referencesVersions);

        if (!repairSkipped)
        {
            var (repaired, configFixes) = RepairConfig(config, inventory);
            if (configFixes.Count > 0)
            {
                BackupConfigFile();
                await _configStore.SaveAsync(repaired, cancellationToken).ConfigureAwait(false);
                fixes.AddRange(configFixes);
            }
        }

        var remainingInventory = await _inventoryService.ScanAsync(cancellationToken).ConfigureAwait(false);
        var remainingConfig = await _configStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        var remaining = Diagnose(remainingInventory, remainingConfig);
        if (repairSkipped)
        {
            remaining.Add(new DoctorIssue(
                "repair-skipped",
                "Config repair skipped: inventory looks degraded (missing registered roots or no SDKs detected). Reconnect the missing roots and re-run 'dotver doctor --fix'."));
        }

        return new DoctorResult(remaining, fixes);
    }

    private void BackupConfigFile()
    {
        try
        {
            if (File.Exists(_paths.ConfigFile))
            {
                File.Copy(_paths.ConfigFile, _paths.ConfigFile + ".bak", overwrite: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A failed backup must not block the repair; the save itself is atomic.
        }
    }

    private List<DoctorIssue> Diagnose(IReadOnlyList<SdkInstallation> inventory, DotVerConfig config)
    {
        var issues = new List<DoctorIssue>();
        var versions = inventory
            .Select(static sdk => sdk.Version.Raw)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(config.CurrentVersion) && !versions.Contains(config.CurrentVersion))
        {
            issues.Add(new DoctorIssue("current-missing", $"Persisted current version {TerminalText.Sanitize(config.CurrentVersion)} is not installed."));
        }

        if (!string.IsNullOrWhiteSpace(config.DefaultVersion) && !versions.Contains(config.DefaultVersion))
        {
            issues.Add(new DoctorIssue("default-missing", $"Persisted default version {TerminalText.Sanitize(config.DefaultVersion)} is not installed."));
        }

        foreach (var alias in config.Aliases.OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (!versions.Contains(alias.Value))
            {
                issues.Add(new DoctorIssue("alias-missing", $"Alias '{TerminalText.Sanitize(alias.Key)}' points to {TerminalText.Sanitize(alias.Value)}, which is not installed."));
            }
        }

        foreach (var root in config.RegisteredRoots)
        {
            if (!Directory.Exists(root))
            {
                issues.Add(new DoctorIssue("root-missing", $"Registered root '{TerminalText.Sanitize(root)}' does not exist."));
            }
        }

        if (inventory.Any(static sdk => !sdk.CanBeUsedDirectly))
        {
            issues.Add(new DoctorIssue("multi-sdk-root", "There are multi-SDK roots that serve only for detection/inspection."));
        }

        foreach (var directory in EnumerateManagedDirectories())
        {
            var name = Path.GetFileName(directory);
            // Mirror inventory parsing: the arch suffix is appended last.
            var separator = name.LastIndexOf("--", StringComparison.Ordinal);
            var versionPart = separator > 0 && SdkVersion.TryParse(name[..separator], out _) ? name[..separator] : name;
            var hostFile = Path.Combine(directory, _environment.IsWindows ? "dotnet.exe" : "dotnet");
            var sdkDir = Path.Combine(directory, "sdk", versionPart);
            if (!SdkVersion.TryParse(versionPart, out _) || !File.Exists(hostFile) || !Directory.Exists(sdkDir))
            {
                issues.Add(new DoctorIssue("incomplete-managed", $"Managed install directory '{TerminalText.Sanitize(name)}' looks incomplete (missing host or sdk folder). Remove it manually if it is not an install in progress."));
            }
        }

        var shimFile = Path.Combine(_paths.Shims, _environment.IsWindows ? "dotnet.cmd" : "dotnet");
        if (!File.Exists(shimFile))
        {
            issues.Add(new DoctorIssue("shims-missing", $"Shim file '{TerminalText.Sanitize(shimFile)}' is missing; run 'dotver doctor --fix'."));
        }

        var pin = _globalJsonService.TryFind(_environment.CurrentDirectory);
        if (pin is not null && GlobalJsonMatcher.Match(pin, inventory, _environment.RuntimeArchitecture) is null)
        {
            issues.Add(new DoctorIssue("globaljson-unsatisfied", $"global.json pins {pin.Version.Raw} but no installed SDK satisfies it; run 'dotver ensure'."));
        }

        return issues;
    }

    private IEnumerable<string> EnumerateManagedDirectories()
    {
        if (!Directory.Exists(_paths.ManagedInstalls))
        {
            return [];
        }

        try
        {
            return Directory.EnumerateDirectories(_paths.ManagedInstalls).ToList();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static (DotVerConfig Config, List<string> Fixes) RepairConfig(
        DotVerConfig config,
        IReadOnlyList<SdkInstallation> inventory)
    {
        var fixes = new List<string>();
        var versions = inventory
            .Select(static sdk => sdk.Version.Raw)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var current = config.CurrentVersion;
        if (!string.IsNullOrWhiteSpace(current) && !versions.Contains(current))
        {
            fixes.Add($"Cleared current ({TerminalText.Sanitize(current)} is not installed).");
            current = null;
        }

        var @default = config.DefaultVersion;
        if (!string.IsNullOrWhiteSpace(@default) && !versions.Contains(@default))
        {
            fixes.Add($"Cleared default ({TerminalText.Sanitize(@default)} is not installed).");
            @default = null;
        }

        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in config.Aliases)
        {
            if (versions.Contains(pair.Value))
            {
                aliases[pair.Key] = pair.Value;
            }
            else
            {
                fixes.Add($"Removed alias '{TerminalText.Sanitize(pair.Key)}' (points to missing {TerminalText.Sanitize(pair.Value)}).");
            }
        }

        // Registered roots are never auto-removed: a missing root may just be
        // a disconnected drive. They are reported by Diagnose only.
        return (new DotVerConfig(current, @default, aliases, [.. config.RegisteredRoots]), fixes);
    }
}
