using DotVer.Application;
using DotVer.Domain;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DotVer.Cli;

internal sealed class EnsureCommand : AsyncCommandBase<EnsureSettings>
{
    private readonly IEnsureService _ensureService;
    private readonly IWorkspaceService _workspaceService;
    private readonly IEnvironmentContext _environment;

    public EnsureCommand(
        IAnsiConsole console,
        IEnsureService ensureService,
        IWorkspaceService workspaceService,
        IEnvironmentContext environment)
        : base(console)
    {
        _ensureService = ensureService;
        _workspaceService = workspaceService;
        _environment = environment;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, EnsureSettings settings)
    {
        if (settings.Recursive)
        {
            if (settings.Use)
            {
                throw new InvalidOperationException("--use does not apply to --recursive (it could point at many SDKs).");
            }

            var root = string.IsNullOrWhiteSpace(settings.Path) ? _environment.CurrentDirectory : settings.Path;
            using var cancellation = CtrlCCancellation.Create();
            var result = await _workspaceService.EnsureRecursiveAsync(root, cancellation.Token).ConfigureAwait(false);
            var missing = result.Scan.Entries.Count(static entry => entry.Invalid || entry.RequestedVersion is not null && !entry.Satisfied);
            var exitCode = missing > 0 || result.Errors.Count > 0 ? 1 : 0;

            if (settings.Json)
            {
                global::System.Console.Out.WriteLine(JsonReports.ForScan(result.Scan, result.InstalledVersions, result.Errors));
                return exitCode;
            }

            ScanCommand.Render(Console, result.Scan);
            foreach (var version in result.InstalledVersions)
            {
                Console.MarkupLine($"[green]Installed[/] [aqua]{Markup.Escape(version)}[/]");
            }

            foreach (var error in result.Errors)
            {
                Console.MarkupLine($"[red]Error:[/] {Markup.Escape(error)}");
            }

            return exitCode;
        }

        var outcome = await _ensureService.EnsureAsync(settings.Use, settings.Path, CancellationToken.None).ConfigureAwait(false);

        if (settings.Json)
        {
            global::System.Console.Out.WriteLine(JsonReports.ForEnsure(outcome));
            return 0;
        }

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Step");
        table.AddColumn("Value");
        table.AddRow(new Text("global.json"), new Text(outcome.Request.FilePath ?? "(unknown)"));
        table.AddRow(new Text("Requested"), new Text(outcome.Request.Version.Raw));
        table.AddRow(new Text("Resolved"), new Text(outcome.Installation.Version.Raw));
        table.AddRow(new Text("Status"), new Text(outcome.InstalledNow ? "Installed now" : "Already installed"));
        table.AddRow(new Text("Host root"), new Text(outcome.Installation.HostRoot));
        Console.Write(table);

        if (!string.IsNullOrWhiteSpace(outcome.Installation.Version.Suffix))
        {
            Console.MarkupLine("[yellow]Note:[/] the pinned SDK is a prerelease build.");
        }

        return 0;
    }
}
