using DotVer.Domain;

namespace DotVer.Application;

public sealed class StateService : IStateService
{
    private readonly IConfigStore _configStore;
    private readonly IVersionResolver _versionResolver;
    private readonly IInventoryService _inventoryService;
    private readonly DotVerPaths _paths;

    public StateService(IConfigStore configStore, IVersionResolver versionResolver, IInventoryService inventoryService, DotVerPaths paths)
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

    public async Task AddAliasAsync(string alias, string versionToken, CancellationToken cancellationToken)
    {
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
