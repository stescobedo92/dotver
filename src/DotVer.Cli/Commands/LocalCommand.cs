using DotVer.Application;
using DotVer.Domain;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DotVer.Cli;

internal sealed class LocalCommand : AsyncCommandBase<LocalSettings>
{
    private readonly IGlobalJsonService _globalJsonService;
    private readonly IInventoryService _inventoryService;
    private readonly IStateService _stateService;
    private readonly IVersionResolver _versionResolver;
    private readonly IEnvironmentContext _environment;

    public LocalCommand(
        IAnsiConsole console,
        IGlobalJsonService globalJsonService,
        IInventoryService inventoryService,
        IStateService stateService,
        IVersionResolver versionResolver,
        IEnvironmentContext environment)
        : base(console)
    {
        _globalJsonService = globalJsonService;
        _inventoryService = inventoryService;
        _stateService = stateService;
        _versionResolver = versionResolver;
        _environment = environment;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, LocalSettings settings)
    {
        var directory = _environment.CurrentDirectory;

        if (settings.Clear)
        {
            if (!string.IsNullOrWhiteSpace(settings.Version) || !string.IsNullOrWhiteSpace(settings.RollForward))
            {
                throw new InvalidOperationException("--clear cannot be combined with a version or --roll-forward.");
            }

            var cleared = _globalJsonService.ClearPin(directory, settings.Parent);
            if (settings.Json)
            {
                global::System.Console.Out.WriteLine(JsonReports.ForLocal(cleared.FilePath, null, null, cleared.Cleared, installed: false));
                return 0;
            }

            if (cleared.Cleared)
            {
                Console.MarkupLine($"[green]Pin removed[/] [grey]{Markup.Escape(cleared.FilePath)}[/]{(cleared.FileDeleted ? " [grey](file deleted)[/]" : string.Empty)}");
            }
            else
            {
                Console.MarkupLine($"[yellow]No sdk pin to remove in[/] {Markup.Escape(cleared.FilePath)}");
            }

            return 0;
        }

        SdkRollForward? rollForward = null;
        if (!string.IsNullOrWhiteSpace(settings.RollForward))
        {
            if (!GlobalJsonService.TryParseRollForward(settings.RollForward, out var parsed))
            {
                throw new InvalidOperationException(
                    $"Unknown roll-forward policy '{settings.RollForward}'. Valid values: disable, patch, feature, minor, major, latestPatch, latestFeature, latestMinor, latestMajor.");
            }

            rollForward = parsed;
        }

        if (string.IsNullOrWhiteSpace(settings.Version))
        {
            var existing = _globalJsonService.TryFind(directory);
            if (existing is null)
            {
                if (rollForward is not null)
                {
                    throw new InvalidOperationException("--roll-forward requires a version argument or an existing global.json pin.");
                }

                if (settings.Json)
                {
                    global::System.Console.Out.WriteLine(JsonReports.ForLocal(null, null, null, cleared: false, installed: false));
                    return 0;
                }

                Console.MarkupLine("[yellow]No global.json pin found for this directory.[/] Use [aqua]dotver local <version>[/] to create one.");
                return 0;
            }

            if (rollForward is null)
            {
                Report(existing.FilePath, existing.Version.Raw, existing.RollForward, installed: true, settings.Json);
                return 0;
            }

            var pinDirectory = Path.GetDirectoryName(existing.FilePath!)!;
            var pathComparison = _environment.IsWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!settings.Parent &&
                !string.Equals(Path.GetFullPath(pinDirectory), Path.GetFullPath(directory), pathComparison))
            {
                throw new InvalidOperationException(
                    $"The effective pin lives at '{existing.FilePath}'. Re-run with --parent to modify that file, or pass a version to create a pin in this directory.");
            }

            var updated = _globalJsonService.WritePin(pinDirectory, existing.Version, rollForward);
            Report(updated.FilePath, updated.Version, updated.RollForward, installed: true, settings.Json);
            return 0;
        }

        var inventory = await _inventoryService.ScanAsync(CancellationToken.None).ConfigureAwait(false);
        var config = await _stateService.GetConfigAsync(CancellationToken.None).ConfigureAwait(false);
        var installedSdk = _versionResolver.FindByToken(settings.Version, config, inventory);

        SdkVersion pinVersion;
        var installed = installedSdk is not null;
        if (installedSdk is not null)
        {
            pinVersion = installedSdk.Version;
        }
        else if (SdkVersion.TryParse(settings.Version, out var exact))
        {
            // Pinning an SDK that is not installed yet is valid for shared repos;
            // `dotver ensure` materializes it on each machine.
            pinVersion = exact!;
        }
        else
        {
            throw new InvalidOperationException(
                $"'{settings.Version}' is neither an installed version/alias nor an exact SDK version. Run 'dotver install {settings.Version}' first.");
        }

        var result = _globalJsonService.WritePin(directory, pinVersion, rollForward);
        Report(result.FilePath, result.Version, result.RollForward, installed, settings.Json);
        return 0;
    }

    private void Report(string? filePath, string? version, SdkRollForward? rollForward, bool installed, bool json)
    {
        var rollForwardText = rollForward is null or SdkRollForward.Default
            ? null
            : GlobalJsonService.FormatRollForward(rollForward.Value);

        if (json)
        {
            global::System.Console.Out.WriteLine(JsonReports.ForLocal(filePath, version, rollForwardText, cleared: false, installed));
            return;
        }

        Console.MarkupLine($"[green]Local[/] -> [aqua]{Markup.Escape(version ?? "(none)")}[/]{(rollForwardText is null ? string.Empty : $" [grey](rollForward: {Markup.Escape(rollForwardText)})[/]")}");
        Console.MarkupLine($"[grey]{Markup.Escape(filePath ?? string.Empty)}[/]");
        if (!installed)
        {
            Console.MarkupLine("[yellow]That SDK is not installed yet.[/] Run [aqua]dotver ensure[/] to install it from this pin.");
        }
    }
}
