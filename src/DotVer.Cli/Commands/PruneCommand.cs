using System.Globalization;
using DotVer.Application;
using DotVer.Domain;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DotVer.Cli;

internal sealed class PruneCommand : AsyncCommandBase<PruneSettings>
{
    private readonly IPruneService _pruneService;

    public PruneCommand(IAnsiConsole console, IPruneService pruneService)
        : base(console)
    {
        _pruneService = pruneService;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, PruneSettings settings)
    {
        var execute = settings.KeepLatestPerChannel && settings.Yes;
        var result = await _pruneService.PruneAsync(settings.KeepLatestPerChannel, execute, CancellationToken.None).ConfigureAwait(false);
        var exitCode = result.Entries.Any(static entry => entry.Action == PruneAction.Failed) ? 1 : 0;

        if (settings.Json)
        {
            global::System.Console.Out.WriteLine(JsonReports.ForPrune(result));
            return exitCode;
        }

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Version");
        table.AddColumn("Arch");
        table.AddColumn("Channel");
        table.AddColumn("Size");
        table.AddColumn("Action");
        foreach (var entry in result.Entries)
        {
            var action = entry.Reason is null
                ? entry.Action.ToString()
                : $"{entry.Action} ({entry.Reason})";
            table.AddRow(
                new Text(entry.Version),
                new Text(entry.Architecture ?? "host"),
                new Text(entry.Channel),
                new Text(FormatBytes(entry.SizeBytes)),
                new Text(action));
        }

        Console.Write(table);
        Console.MarkupLine(
            $"Total managed: [aqua]{FormatBytes(result.TotalBytes)}[/]  Reclaimable: [aqua]{FormatBytes(result.ReclaimableBytes)}[/]{(result.DryRun && settings.KeepLatestPerChannel ? "  [yellow](dry run: pass --yes to delete)[/]" : string.Empty)}");

        if (!settings.KeepLatestPerChannel)
        {
            Console.MarkupLine("Add [aqua]--keep-latest-per-channel --yes[/] to delete older patches per channel.");
        }

        return exitCode;
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return value.ToString("0.#", CultureInfo.InvariantCulture) + " " + units[unit];
    }
}
