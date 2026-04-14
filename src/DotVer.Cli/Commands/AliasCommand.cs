using DotVer.Application;
using DotVer.Domain;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DotVer.Cli;

internal sealed class AliasCommand : AsyncCommandBase<AliasSettings>
{
    private readonly IStateService _stateService;

    public AliasCommand(IAnsiConsole console, IStateService stateService)
        : base(console)
    {
        _stateService = stateService;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, AliasSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Name) || string.IsNullOrWhiteSpace(settings.Version))
        {
            var config = await _stateService.GetConfigAsync(CancellationToken.None).ConfigureAwait(false);
            var table = new Table();
            table.AddColumn("Alias");
            table.AddColumn("Version");
            foreach (var alias in config.Aliases.OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase))
            {
                table.AddRow(alias.Key, alias.Value);
            }

            Console.Write(table);
            return 0;
        }

        await _stateService.AddAliasAsync(settings.Name, settings.Version, CancellationToken.None).ConfigureAwait(false);
        Console.MarkupLine($"[green]Alias[/] [aqua]{settings.Name}[/] -> [aqua]{settings.Version}[/]");
        return 0;
    }
}
