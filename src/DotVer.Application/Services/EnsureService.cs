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

    public async Task<EnsureOutcome> EnsureAsync(bool setCurrent, string? directory, CancellationToken cancellationToken)
    {
        var startDirectory = string.IsNullOrWhiteSpace(directory)
            ? _environment.CurrentDirectory
            : Path.GetFullPath(directory);
        if (!Directory.Exists(startDirectory))
        {
            throw new InvalidOperationException($"Directory '{startDirectory}' does not exist.");
        }

        var request = _globalJsonService.TryFind(startDirectory)
            ?? throw new InvalidOperationException(
                $"No global.json pin found at or above '{startDirectory}'. Create one with 'dotver local <version>'.");

        var inventory = await _inventoryService.ScanAsync(cancellationToken).ConfigureAwait(false);
        var matched = GlobalJsonMatcher.Match(request, inventory, _environment.RuntimeArchitecture);
        var installedNow = false;

        if (matched is null)
        {
            // request.Version was validated by SdkVersion.TryParse inside TryFind,
            // and InstallService passes it as a single process argument (no shell).
            await _installService.InstallAsync(request.Version.Raw, options: null, cancellationToken).ConfigureAwait(false);
            installedNow = true;

            inventory = await _inventoryService.ScanAsync(cancellationToken).ConfigureAwait(false);
            matched = GlobalJsonMatcher.Match(request, inventory, _environment.RuntimeArchitecture)
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

