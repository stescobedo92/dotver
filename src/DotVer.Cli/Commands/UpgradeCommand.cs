using DotVer.Application;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DotVer.Cli;

internal sealed class UpgradeCommand : AsyncCommandBase<UpgradeSettings>
{
    private readonly IOutdatedService _outdatedService;

    public UpgradeCommand(IAnsiConsole console, IOutdatedService outdatedService)
        : base(console)
    {
        _outdatedService = outdatedService;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, UpgradeSettings settings)
    {
        var outcomes = await _outdatedService
            .UpgradeAsync(settings.Token, settings.All, settings.PruneOld, CancellationToken.None)
            .ConfigureAwait(false);

        var exitCode = outcomes.Any(static outcome => outcome.Error is not null) ? 1 : 0;

        if (settings.Json)
        {
            global::System.Console.Out.WriteLine(JsonReports.ForUpgrade(outcomes));
            return exitCode;
        }

        if (outcomes.Count == 0)
        {
            Console.MarkupLine("[green]No managed SDKs need upgrading.[/]");
            return 0;
        }

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Channel");
        table.AddColumn("From");
        table.AddColumn("To");
        table.AddColumn("Result");
        foreach (var outcome in outcomes)
        {
            var result = outcome.Error is not null
                ? "failed"
                : outcome.Upgraded
                    ? "upgraded" + (outcome.PrunedOld ? " (old pruned)" : string.Empty)
                    : "already latest";
            table.AddRow(
                new Text(outcome.Channel),
                new Text(outcome.FromVersion),
                new Text(outcome.ToVersion),
                new Text(result));
        }

        Console.Write(table);

        foreach (var outcome in outcomes)
        {
            if (outcome.Error is not null)
            {
                Console.MarkupLine($"[red]{Markup.Escape(outcome.FromVersion)}:[/] {Markup.Escape(outcome.Error)}");
            }

            if (outcome.Note is not null)
            {
                Console.MarkupLine($"[yellow]Note:[/] {Markup.Escape(outcome.Note)}");
            }
        }

        return exitCode;
    }
}
