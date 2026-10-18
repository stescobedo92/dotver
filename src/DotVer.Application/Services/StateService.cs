using DotVer.Domain;

namespace DotVer.Application;

public sealed class StateService : IStateService
{
    private readonly IConfigStore _configStore;
    private readonly IVersionResolver _versionResolver;
    private readonly IInventoryService _inventoryService;
    private readonly DotVerPaths _paths;
    private readonly IEnvironmentContext _environment;

    public StateService(
        IConfigStore configStore,
        IVersionResolver versionResolver,
        IInventoryService inventoryService,
        DotVerPaths paths,
        IEnvironmentContext environment)
    {
        _configStore = configStore;
        _versionResolver = versionResolver;
        _inventoryService = inventoryService;
        _paths = paths;
        _environment = environment;
    }

    public async Task<DotVerConfig> GetConfigAsync(CancellationToken cancellationToken) =>
        await _configStore.LoadAsync(cancellationToken).ConfigureAwait(false);

    public async Task<SdkInstallation> SetCurrentAsync(string token, CancellationToken cancellationToken)
    {
        var config = await _configStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        var installations = await _inventoryService.ScanAsync(cancellationToken).ConfigureAwait(false);
        var sdk = _versionResolver.FindByToken(token, config, installations)
            ?? throw new InvalidOperationException($"No selectable version found for '{token}'.");

        config = config with { CurrentVersion = sdk.Version.Raw };
        await _configStore.SaveAsync(config, cancellationToken).ConfigureAwait(false);
        return sdk;
    }

    public async Task<SdkInstallation> SetCurrentAndDefaultAsync(string token, CancellationToken cancellationToken)
    {
        var config = await _configStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        var installations = await _inventoryService.ScanAsync(cancellationToken).ConfigureAwait(false);
        var sdk = _versionResolver.FindByToken(token, config, installations)
            ?? throw new InvalidOperationException($"No selectable version found for '{token}'.");

        config = config with { CurrentVersion = sdk.Version.Raw, DefaultVersion = sdk.Version.Raw };
        await _configStore.SaveAsync(config, cancellationToken).ConfigureAwait(false);
        return sdk;
    }

    public async Task<SdkInstallation> SetDefaultAsync(string token, CancellationToken cancellationToken)
    {
        var config = await _configStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        var installations = await _inventoryService.ScanAsync(cancellationToken).ConfigureAwait(false);
        var sdk = _versionResolver.FindByToken(token, config, installations)
            ?? throw new InvalidOperationException($"No selectable version found for '{token}'.");

        config = config with { DefaultVersion = sdk.Version.Raw };
        await _configStore.SaveAsync(config, cancellationToken).ConfigureAwait(false);
        return sdk;
    }

    private static readonly string[] ReservedAliasNames = ["system", "current", "default", "latest"];

    public async Task AddAliasAsync(string alias, string versionToken, CancellationToken cancellationToken)
    {
        if (ReservedAliasNames.Contains(alias.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"'{alias.Trim()}' is a reserved token and cannot be used as an alias (reserved: {string.Join(", ", ReservedAliasNames)}).");
        }

        var config = await _configStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        var installations = await _inventoryService.ScanAsync(cancellationToken).ConfigureAwait(false);
        var sdk = _versionResolver.FindByToken(versionToken, config, installations)
            ?? throw new InvalidOperationException($"No selectable version found for '{versionToken}'.");

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
            throw new InvalidOperationException($"Only dotver-managed SDKs can be uninstalled. Token received: '{token}'.");
        }

        return await UninstallManagedCoreAsync(target, cancellationToken).ConfigureAwait(false);
    }

    public async Task<UninstallOutcome> UninstallManagedAsync(SdkInstallation target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!target.IsManaged)
        {
            throw new InvalidOperationException($"Only dotver-managed SDKs can be uninstalled. Version received: '{target.Version.Raw}'.");
        }

        // Re-resolve by identity (host root) against a fresh scan so a stale
        // snapshot can never delete a different build of the same version.
        var inventory = await _inventoryService.ScanAsync(cancellationToken).ConfigureAwait(false);
        var comparison = _environment.IsWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var current = inventory.FirstOrDefault(sdk =>
            sdk.IsManaged && string.Equals(sdk.HostRoot, target.HostRoot, comparison))
            ?? throw new InvalidOperationException($"SDK {target.Version.Raw} is no longer installed.");

        return await UninstallManagedCoreAsync(current, cancellationToken).ConfigureAwait(false);
    }

    private async Task<UninstallOutcome> UninstallManagedCoreAsync(SdkInstallation target, CancellationToken cancellationToken)
    {
        var config = await _configStore.LoadAsync(cancellationToken).ConfigureAwait(false);

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
            DeleteManagedInstallDirectory(target);
        }

        // The automatic fallback must never land on a foreign-architecture
        // install (same rule as install auto-selection).
        var remainingInventory = (await _inventoryService.ScanAsync(cancellationToken).ConfigureAwait(false))
            .Where(sdk => sdk.IsManaged && Architectures.MatchesHost(sdk.Architecture, _environment.RuntimeArchitecture))
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

    public async Task<bool> ClearCurrentAsync(CancellationToken cancellationToken)
    {
        var config = await _configStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(config.CurrentVersion))
        {
            return false;
        }

        await _configStore.SaveAsync(config with { CurrentVersion = null }, cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<bool> ClearDefaultAsync(CancellationToken cancellationToken)
    {
        var config = await _configStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(config.DefaultVersion))
        {
            return false;
        }

        await _configStore.SaveAsync(config with { DefaultVersion = null }, cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<RepointResult> RepointVersionAsync(string fromVersion, string toVersion, CancellationToken cancellationToken)
    {
        var config = await _configStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        var installations = await _inventoryService.ScanAsync(cancellationToken).ConfigureAwait(false);
        var sdk = _versionResolver.FindByToken(toVersion, config, installations)
            ?? throw new InvalidOperationException($"No selectable version found for '{toVersion}'.");

        var repointedCurrent = string.Equals(config.CurrentVersion, fromVersion, StringComparison.OrdinalIgnoreCase);
        var repointedDefault = string.Equals(config.DefaultVersion, fromVersion, StringComparison.OrdinalIgnoreCase);
        var repointedAliases = config.Aliases
            .Where(pair => string.Equals(pair.Value, fromVersion, StringComparison.OrdinalIgnoreCase))
            .Select(static pair => pair.Key)
            .ToList();

        foreach (var alias in repointedAliases)
        {
            config.Aliases[alias] = sdk.Version.Raw;
        }

        config = config with
        {
            CurrentVersion = repointedCurrent ? sdk.Version.Raw : config.CurrentVersion,
            DefaultVersion = repointedDefault ? sdk.Version.Raw : config.DefaultVersion
        };

        // One save for current+default+aliases: no torn state between mutations.
        await _configStore.SaveAsync(config, cancellationToken).ConfigureAwait(false);
        return new RepointResult(repointedCurrent, repointedDefault, repointedAliases);
    }

    private void DeleteManagedInstallDirectory(SdkInstallation target)
    {
        var comparison = _environment.IsWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var managedRoot = Path.GetFullPath(_paths.ManagedInstalls).TrimEnd(Path.DirectorySeparatorChar);
        var hostRoot = Path.GetFullPath(target.HostRoot).TrimEnd(Path.DirectorySeparatorChar);
        var parent = Path.GetDirectoryName(hostRoot);

        // Recursive delete is only ever allowed on the exact managed layout:
        // <ManagedInstalls>/<version> (or <version>--<arch>), a real directory
        // (never a link/junction).
        var expectedLeaf = target.Architecture is null ||
                           Architectures.MatchesHost(target.Architecture, _environment.RuntimeArchitecture)
            ? target.Version.Raw
            : $"{target.Version.Raw}--{target.Architecture}";
        if (!string.Equals(parent, managedRoot, comparison) ||
            !string.Equals(Path.GetFileName(hostRoot), expectedLeaf, comparison))
        {
            throw new InvalidOperationException(
                $"Refusing to delete '{TerminalText.Sanitize(hostRoot)}': it is not a direct managed install under '{TerminalText.Sanitize(managedRoot)}'.");
        }

        var info = new DirectoryInfo(hostRoot);
        if (info.LinkTarget is not null || (info.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException(
                $"Refusing to delete '{TerminalText.Sanitize(hostRoot)}': it is a symbolic link or junction. Remove it manually if intended.");
        }

        Directory.Delete(hostRoot, recursive: true);
    }
}
