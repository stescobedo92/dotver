using DotVer.Application;
using DotVer.Domain;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DotVer.Cli;

internal sealed class ScanCommand : AsyncCommandBase<ScanSettings>
{
    private readonly IWorkspaceService _workspaceService;
    private readonly IEnvironmentContext _environment;

    public ScanCommand(IAnsiConsole console, IWorkspaceService workspaceService, IEnvironmentContext environment)
        : base(console)
    {
        _workspaceService = workspaceService;
        _environment = environment;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, ScanSettings settings)
    {
        var root = string.IsNullOrWhiteSpace(settings.Path) ? _environment.CurrentDirectory : settings.Path;
        using var cancellation = CtrlCCancellation.Create();
        var scan = await _workspaceService.ScanAsync(root, cancellation.Token).ConfigureAwait(false);
        var missing = scan.Entries.Count(static entry => entry.Invalid || entry.RequestedVersion is not null && !entry.Satisfied);
        var exitCode = missing > 0 || scan.Truncated ? 1 : 0;

        if (settings.Json)
        {
            global::System.Console.Out.WriteLine(JsonReports.ForScan(scan));
            return exitCode;
        }

        Render(Console, scan);
        return exitCode;
    }

    internal static void Render(IAnsiConsole console, WorkspaceScanResult scan)
    {
        var pins = scan.Entries.Where(static entry => entry.RequestedVersion is not null).ToList();
        if (pins.Count == 0 && scan.Entries.All(static entry => !entry.Invalid))
        {
            console.MarkupLine("[grey]No global.json SDK pins found.[/]");
            return;
        }

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Project");
        table.AddColumn("Pinned");
        table.AddColumn("Matched");
        table.AddColumn("Status");
        foreach (var entry in pins)
        {
            table.AddRow(
                new Text(Sanitize(Path.GetRelativePath(scan.Root, entry.FilePath))),
                new Text(entry.RequestedVersion!),
                new Text(entry.MatchedVersion ?? "-"),
                new Text(entry.Satisfied ? "satisfied" : "missing"));
        }

        foreach (var entry in scan.Entries.Where(static entry => entry.Invalid))
        {
            table.AddRow(
                new Text(Sanitize(Path.GetRelativePath(scan.Root, entry.FilePath))),
                new Text("(invalid)"),
                new Text("-"),
                new Text("invalid"));
        }

        console.Write(table);
        if (scan.Truncated)
        {
            console.MarkupLine("[yellow]Scan truncated at the file limit; narrow the root directory.[/]");
        }
    }

    private static string Sanitize(string value) =>
        new(value.Where(static c => !char.IsControl(c)).ToArray());
}
