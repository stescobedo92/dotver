using DotVer.Application;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DotVer.Cli;

internal sealed class EnsureCommand : AsyncCommandBase<EnsureSettings>
{
    private readonly IEnsureService _ensureService;

    public EnsureCommand(IAnsiConsole console, IEnsureService ensureService)
        : base(console)
    {
        _ensureService = ensureService;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, EnsureSettings settings)
    {
        var outcome = await _ensureService.EnsureAsync(settings.Use, CancellationToken.None).ConfigureAwait(false);

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
