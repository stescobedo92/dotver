using DotVer.Application;
using DotVer.Domain;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DotVer.Cli;

internal sealed class UseCommand : AsyncCommandBase<VersionTokenSettings>
{
    private readonly IStateService _stateService;

    public UseCommand(IAnsiConsole console, IStateService stateService)
        : base(console)
    {
        _stateService = stateService;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, VersionTokenSettings settings)
    {
        var sdk = await _stateService.SetCurrentAsync(settings.Version, CancellationToken.None).ConfigureAwait(false);
        Console.MarkupLine($"[green]Current[/] -> [aqua]{sdk.Version.Raw}[/]");
        return 0;
    }
}
