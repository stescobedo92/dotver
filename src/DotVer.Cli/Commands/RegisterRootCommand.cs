using DotVer.Application;
using DotVer.Domain;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DotVer.Cli;

internal sealed class RegisterRootCommand : AsyncCommandBase<RegisterRootSettings>
{
    private readonly IStateService _stateService;

    public RegisterRootCommand(IAnsiConsole console, IStateService stateService)
        : base(console)
    {
        _stateService = stateService;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, RegisterRootSettings settings)
    {
        await _stateService.RegisterRootAsync(settings.Path, CancellationToken.None).ConfigureAwait(false);
        Console.MarkupLine($"[green]Root registered:[/] {settings.Path}");
        return 0;
    }
}
