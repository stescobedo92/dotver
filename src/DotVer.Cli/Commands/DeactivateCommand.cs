using DotVer.Application;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DotVer.Cli;

internal sealed class DeactivateCommand : AsyncCommandBase
{
    private readonly IStateService _stateService;

    public DeactivateCommand(IAnsiConsole console, IStateService stateService)
        : base(console)
    {
        _stateService = stateService;
    }

    public override async Task<int> ExecuteAsync(CommandContext context)
    {
        var cleared = await _stateService.ClearCurrentAsync(CancellationToken.None).ConfigureAwait(false);
        Console.MarkupLine(cleared
            ? "[green]Current cleared[/] [grey](selection now falls back to global.json, then default, then system)[/]"
            : "[grey]No persisted current selection to clear.[/]");
        return 0;
    }
}
