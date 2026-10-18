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

        // Explicit per-process override (set by `dotver exec`) wins over everything.
        // Only exact installed versions are honored (that is all `exec` emits):
        // no aliases/prefixes, and an unresolvable value fails loudly instead of
        // silently falling through to another SDK.
        var overrideToken = _environment.GetEnvironmentVariable("DOTVER_VERSION");
        if (!string.IsNullOrWhiteSpace(overrideToken))
        {
            // Explicit host preference: do not depend on inventory ordering to
            // avoid a foreign managed build winning over the native one.
            var overridden = SdkVersion.TryParse(overrideToken.Trim(), out _)
                ? installations
                    .Where(sdk => string.Equals(sdk.Version.Raw, overrideToken.Trim(), StringComparison.OrdinalIgnoreCase))
                    .OrderBy(sdk => Architectures.MatchesHost(sdk.Architecture, _environment.RuntimeArchitecture) ? 0 : 1)
                    .FirstOrDefault()
                : null;

            return overridden is not null
                ? new SdkSelection(overridden, $"Selected by DOTVER_VERSION ({overridden.Version.Raw})", false)
                : throw new InvalidOperationException(
                    $"DOTVER_VERSION is set to '{Sanitize(overrideToken)}' but that exact SDK version is not installed. Unset DOTVER_VERSION or install it first.");
        }

        // A project pin wins inside its directory tree; otherwise the shim could
        // never honor global.json once a global current/default exists.
        var globalJson = _globalJsonService.TryFind(_environment.CurrentDirectory);
        if (globalJson is not null)
        {
            var pinned = GlobalJsonMatcher.Match(globalJson, installations, _environment.RuntimeArchitecture);
            if (pinned is not null)
            {
                return new SdkSelection(pinned, $"Selected by global.json ({globalJson.Version.Raw})", true);
            }
        }

        // Persisted current/default are automatic selection: a foreign-arch
        // install left behind after the native one was removed must not be
        // silently adopted by the shim.
        if (!string.IsNullOrWhiteSpace(config.CurrentVersion))
        {
            var current = _versionResolver.FindByToken(config.CurrentVersion, config, installations);
            if (current is not null && Architectures.MatchesHost(current.Architecture, _environment.RuntimeArchitecture))
            {
                return new SdkSelection(current, $"Selected by persisted 'use' ({config.CurrentVersion})", false);
            }
        }

        if (!string.IsNullOrWhiteSpace(config.DefaultVersion))
        {
            var fallback = _versionResolver.FindByToken(config.DefaultVersion, config, installations);
            if (fallback is not null && Architectures.MatchesHost(fallback.Architecture, _environment.RuntimeArchitecture))
            {
                return new SdkSelection(fallback, $"Selected by default ({config.DefaultVersion})", false);
            }
        }

        var systemPath = _inventoryService.GetSystemDotnetPath();
        var systemRootSelection = installations.FirstOrDefault(sdk =>
            string.Equals(sdk.DotnetPath, systemPath, _environment.IsWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));

        return new SdkSelection(systemRootSelection, "Selected by system fallback", false);
    }

    private static string Sanitize(string value) =>
        new(value.Trim().Where(static c => !char.IsControl(c)).ToArray());
}
