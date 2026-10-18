using DotVer.Application;
using DotVer.Domain;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DotVer.Cli;

internal sealed class UseCommand : AsyncCommandBase<VersionTokenSettings>
{
    private readonly IStateService _stateService;
    private readonly IGlobalJsonService _globalJsonService;
    private readonly IEnvironmentContext _environment;

    public UseCommand(
        IAnsiConsole console,
        IStateService stateService,
        IGlobalJsonService globalJsonService,
        IEnvironmentContext environment)
        : base(console)
    {
        _stateService = stateService;
        _globalJsonService = globalJsonService;
        _environment = environment;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, VersionTokenSettings settings)
    {
        var sdk = await _stateService.SetCurrentAsync(settings.Version, CancellationToken.None).ConfigureAwait(false);
        Console.MarkupLine($"[green]Current[/] -> [aqua]{Markup.Escape(sdk.Version.Raw)}[/]");

        var pin = _globalJsonService.TryFind(_environment.CurrentDirectory);
        if (pin is not null && !string.Equals(pin.Version.Raw, sdk.Version.Raw, StringComparison.OrdinalIgnoreCase))
        {
            Console.MarkupLine(
                $"[yellow]Note:[/] this directory is pinned to [aqua]{Markup.Escape(pin.Version.Raw)}[/] by global.json, which wins here. Use [aqua]dotver local[/] to change the project pin.");
        }

        return 0;
    }
}
