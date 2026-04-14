using DotVer.Application;
using DotVer.Domain;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DotVer.Cli;

internal sealed class UnaliasCommand : AsyncCommandBase<UnaliasSettings>
{
    private readonly IStateService _stateService;

    public UnaliasCommand(IAnsiConsole console, IStateService stateService)
        : base(console)
    {
        _stateService = stateService;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, UnaliasSettings settings)
    {
        await _stateService.RemoveAliasAsync(settings.Name, CancellationToken.None).ConfigureAwait(false);
        Console.MarkupLine($"[green]Alias removed:[/] {settings.Name}");
        return 0;
    }
}
