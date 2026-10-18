using DotVer.Domain;

namespace DotVer.Application;

public sealed class EnsureService : IEnsureService
{
    private readonly IGlobalJsonService _globalJsonService;
    private readonly IInventoryService _inventoryService;
    private readonly IInstallService _installService;
    private readonly IStateService _stateService;
    private readonly IEnvironmentContext _environment;

    public EnsureService(
        IGlobalJsonService globalJsonService,
        IInventoryService inventoryService,
        IInstallService installService,
        IStateService stateService,
        IEnvironmentContext environment)
    {
        _globalJsonService = globalJsonService;
        _inventoryService = inventoryService;
        _installService = installService;
        _stateService = stateService;
        _environment = environment;
    }

    public async Task<EnsureOutcome> EnsureAsync(bool setCurrent, CancellationToken cancellationToken)
    {
        var request = _globalJsonService.TryFind(_environment.CurrentDirectory)
            ?? throw new InvalidOperationException(
                $"No global.json pin found at or above '{_environment.CurrentDirectory}'. Create one with 'dotver local <version>'.");

        var inventory = await _inventoryService.ScanAsync(cancellationToken).ConfigureAwait(false);
        var matched = GlobalJsonMatcher.Match(request, inventory);
        var installedNow = false;

        if (matched is null)
        {
            // request.Version was validated by SdkVersion.TryParse inside TryFind,
            // and InstallService passes it as a single process argument (no shell).
            await _installService.InstallAsync(request.Version.Raw, cancellationToken).ConfigureAwait(false);
            installedNow = true;

            inventory = await _inventoryService.ScanAsync(cancellationToken).ConfigureAwait(false);
            matched = GlobalJsonMatcher.Match(request, inventory)
                ?? throw new InvalidOperationException(
                    $"Installed {request.Version.Raw} but it still does not satisfy global.json (rollForward: {request.RollForward}).");
        }

        if (setCurrent)
        {
            await _stateService.SetCurrentAsync(matched.Version.Raw, cancellationToken).ConfigureAwait(false);
        }

        return new EnsureOutcome(request, matched, installedNow);
    }
}
