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
        if (settings.DryRun && !settings.Ensure)
        {
            throw new InvalidOperationException("--dry-run only applies together with --ensure.");
        }

        var root = string.IsNullOrWhiteSpace(settings.Path) ? _environment.CurrentDirectory : settings.Path;
        using var cancellation = CtrlCCancellation.Create();

        if (settings.Ensure && settings.DryRun)
        {
            var plan = await _workspaceService.PlanEnsureAsync(root, cancellation.Token).ConfigureAwait(false);
            // Same contract as a plain scan: non-zero while something is missing, so CI can gate on it.
            var planExit = plan.MissingVersions.Count > 0 || plan.Errors.Count > 0 ? 1 : 0;
            if (settings.Json)
            {
                global::System.Console.Out.WriteLine(JsonReports.ForScan(plan.Scan, errors: plan.Errors, wouldInstall: plan.MissingVersions));
                return planExit;
            }

            Render(Console, plan.Scan);
            Console.MarkupLine(plan.MissingVersions.Count == 0
                ? "[green]Every pin is satisfied; nothing to install.[/]"
                : $"[yellow]Would install:[/] {Markup.Escape(string.Join(", ", plan.MissingVersions))} [grey](dry run: drop --dry-run to install)[/]");
            RenderErrors(plan.Errors);
            return planExit;
        }

        if (settings.Ensure)
        {
            var result = await _workspaceService.EnsureRecursiveAsync(root, cancellation.Token).ConfigureAwait(false);
            var stillMissing = CountMissing(result.Scan);
            var ensureExit = stillMissing > 0 || result.Errors.Count > 0 ? 1 : 0;
            if (settings.Json)
            {
                global::System.Console.Out.WriteLine(JsonReports.ForScan(result.Scan, result.InstalledVersions, result.Errors));
                return ensureExit;
            }

            Render(Console, result.Scan);
            foreach (var version in result.InstalledVersions)
            {
                Console.MarkupLine($"[green]Installed[/] [aqua]{Markup.Escape(version)}[/]");
            }

            RenderErrors(result.Errors);
            return ensureExit;
        }

        var scan = await _workspaceService.ScanAsync(root, cancellation.Token).ConfigureAwait(false);
        var exitCode = CountMissing(scan) > 0 || scan.Truncated ? 1 : 0;

        if (settings.Json)
        {
            global::System.Console.Out.WriteLine(JsonReports.ForScan(scan));
            return exitCode;
        }

        Render(Console, scan);
        return exitCode;
    }

    private static int CountMissing(WorkspaceScanResult scan) =>
        scan.Entries.Count(static entry => entry.Invalid || entry.RequestedVersion is not null && !entry.Satisfied);

    private void RenderErrors(IReadOnlyList<string> errors)
    {
        foreach (var error in errors)
        {
            Console.MarkupLine($"[red]Error:[/] {Markup.Escape(error)}");
        }
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
