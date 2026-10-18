using DotVer.Application;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DotVer.Cli;

internal sealed class DefaultCommand : AsyncCommandBase<VersionTokenSettings>
{
    private readonly IStateService _stateService;

    public DefaultCommand(IAnsiConsole console, IStateService stateService)
        : base(console)
    {
        _stateService = stateService;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, VersionTokenSettings settings)
    {
        if (string.Equals(settings.Version, "system", StringComparison.OrdinalIgnoreCase))
        {
            var configBefore = await _stateService.GetConfigAsync(CancellationToken.None).ConfigureAwait(false);
            var cleared = await _stateService.ClearDefaultAsync(CancellationToken.None).ConfigureAwait(false);
            Console.MarkupLine(cleared
                ? "[green]Default cleared[/] [grey](selection now falls back to global.json, then system)[/]"
                : "[grey]No persisted default was set.[/]");
            if (configBefore.Aliases.ContainsKey("system"))
            {
                Console.MarkupLine("[yellow]Warning:[/] you also have an alias named [aqua]system[/]; the reserved word shadows it here. Rename it with [aqua]dotver unalias system[/].");
            }

            return 0;
        }

        var sdk = await _stateService.SetDefaultAsync(settings.Version, CancellationToken.None).ConfigureAwait(false);
        Console.MarkupLine($"[green]Default[/] -> [aqua]{Markup.Escape(sdk.Version.Raw)}[/]");
        return 0;
    }
}
