using DotVer.Application;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DotVer.Cli;

internal sealed class ShellCommand : AsyncCommandBase<ShellSettings>
{
    private readonly IShellService _shellService;

    public ShellCommand(IAnsiConsole console, IShellService shellService)
        : base(console)
    {
        _shellService = shellService;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, ShellSettings settings)
    {
        var result = await _shellService.SpawnShellAsync(settings.Token, settings.Shell, CancellationToken.None).ConfigureAwait(false);
        return result.ExitCode;
    }
}
