using DotVer.Application;
using DotVer.Domain;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DotVer.Cli;

internal sealed class OutdatedCommand : AsyncCommandBase<OutdatedSettings>
{
    private readonly IOutdatedService _outdatedService;

    public OutdatedCommand(IAnsiConsole console, IOutdatedService outdatedService)
        : base(console)
    {
        _outdatedService = outdatedService;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, OutdatedSettings settings)
    {
        var entries = await _outdatedService.GetOutdatedAsync(CancellationToken.None).ConfigureAwait(false);
        var filtered = entries
            .Where(entry => !settings.ManagedOnly || entry.Installation.IsManaged)
            .Where(entry => settings.All || entry.NeedsAttention)
            .ToList();

        if (settings.Json)
        {
            global::System.Console.Out.WriteLine(JsonReports.ForOutdated(filtered));
            return 0;
        }

        if (filtered.Count == 0)
        {
            Console.MarkupLine("[green]All detected SDKs are up to date and supported.[/]");
            return 0;
        }

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Version");
        table.AddColumn("Channel");
        table.AddColumn("Latest");
        table.AddColumn("Status");
        table.AddColumn("Managed");
        foreach (var entry in filtered)
        {
            table.AddRow(
                new Text(entry.Installation.Version.Raw),
                new Text(entry.Channel),
                new Text(entry.LatestInChannel ?? "(unknown)"),
                new Text(StatusOf(entry)),
                new Text(entry.Installation.IsManaged ? "yes" : "no"));
        }

        Console.Write(table);
        return 0;
    }

    private static string StatusOf(OutdatedEntry entry)
    {
        var parts = new List<string>();
        if (entry.UpdateAvailable)
        {
            parts.Add("update available");
        }

        if (entry.IsEndOfLife)
        {
            parts.Add("end of life");
        }

        if (entry.IsPrerelease)
        {
            parts.Add("prerelease");
        }

        return parts.Count == 0 ? "up to date" : string.Join(", ", parts);
    }
}
