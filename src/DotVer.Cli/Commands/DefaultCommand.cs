using DotVer.Application;
using DotVer.Domain;
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
        var sdk = await _stateService.SetDefaultAsync(settings.Version, CancellationToken.None).ConfigureAwait(false);
        Console.MarkupLine($"[green]Default[/] -> [aqua]{sdk.Version.Raw}[/]");
        return 0;
    }
}
