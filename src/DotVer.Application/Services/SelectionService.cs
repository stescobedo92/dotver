using DotVer.Domain;

namespace DotVer.Application;

public sealed class SelectionService : ISelectionService
{
    private readonly IInventoryService _inventoryService;
    private readonly IConfigStore _configStore;
    private readonly IGlobalJsonService _globalJsonService;
    private readonly IVersionResolver _versionResolver;
    private readonly IEnvironmentContext _environment;

    public SelectionService(
        IInventoryService inventoryService,
        IConfigStore configStore,
        IGlobalJsonService globalJsonService,
        IVersionResolver versionResolver,
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

        // A project pin wins inside its directory tree; otherwise the shim could
        // never honor global.json once a global current/default exists.
        var globalJson = _globalJsonService.TryFind(_environment.CurrentDirectory);
        if (globalJson is not null)
        {
            var pinned = GlobalJsonMatcher.Match(globalJson, installations);
            if (pinned is not null)
            {
                return new SdkSelection(pinned, $"Selected by global.json ({globalJson.Version.Raw})", true);
            }
        }

        if (!string.IsNullOrWhiteSpace(config.CurrentVersion))
        {
            var current = _versionResolver.FindByToken(config.CurrentVersion, config, installations);
            if (current is not null)
            {
                return new SdkSelection(current, $"Selected by persisted 'use' ({config.CurrentVersion})", false);
            }
        }

        if (!string.IsNullOrWhiteSpace(config.DefaultVersion))
        {
            var fallback = _versionResolver.FindByToken(config.DefaultVersion, config, installations);
            if (fallback is not null)
            {
                return new SdkSelection(fallback, $"Selected by default ({config.DefaultVersion})", false);
            }
        }

        var systemPath = _inventoryService.GetSystemDotnetPath();
        var systemRootSelection = installations.FirstOrDefault(sdk =>
            string.Equals(sdk.DotnetPath, systemPath, _environment.IsWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));

        return new SdkSelection(systemRootSelection, "Selected by system fallback", false);
    }
}
