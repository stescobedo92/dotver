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

        var globalJson = _globalJsonService.TryFind(_environment.CurrentDirectory);
        if (globalJson is not null)
        {
            var selection = MatchGlobalJson(globalJson, installations);
            if (selection is not null)
            {
                return new SdkSelection(selection, $"Selected by global.json ({globalJson.Version.Raw})", true);
            }
        }

        var systemPath = _inventoryService.GetSystemDotnetPath();
        var systemRootSelection = installations.FirstOrDefault(sdk =>
            string.Equals(sdk.DotnetPath, systemPath, _environment.IsWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));

        return new SdkSelection(systemRootSelection, "Selected by system fallback", false);
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
