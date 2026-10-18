using DotVer.Application;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DotVer.Cli;

internal sealed class InfoCommand : AsyncCommandBase<InfoSettings>
{
    private readonly IInfoService _infoService;

    public InfoCommand(IAnsiConsole console, IInfoService infoService)
        : base(console)
    {
        _infoService = infoService;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, InfoSettings settings)
    {
        var info = await _infoService.GetInfoAsync(settings.Token, CancellationToken.None).ConfigureAwait(false);

        if (settings.Json)
        {
            global::System.Console.Out.WriteLine(JsonReports.ForInfo(info));
            return 0;
        }

        // Text cells everywhere: AddRow(string,...) parses Spectre markup, and
        // these values come from directory names on disk (untrusted roots).
        var grid = new Grid();
        grid.AddColumn();
        grid.AddColumn();
        grid.AddRow(new Text("SDK"), new Text(info.Installation.Version.Raw));
        grid.AddRow(new Text("Architecture"), new Text(info.Installation.Architecture ?? "host default"));
        grid.AddRow(new Text("Managed"), new Text(info.Installation.IsManaged ? "yes" : "no"));
        grid.AddRow(new Text("Host root"), new Text(TerminalText.Sanitize(info.Installation.HostRoot)));
        grid.AddRow(new Text("dotnet"), new Text(TerminalText.Sanitize(info.Installation.DotnetPath)));
        foreach (var runtime in info.Runtimes)
        {
            grid.AddRow(new Text(runtime.Kind), new Text(string.Join(", ", runtime.Versions)));
        }

        grid.AddRow(new Text("Workloads"), new Text(info.Workloads.Count == 0 ? "(none detected)" : string.Join(", ", info.Workloads)));

        Console.Write(new Panel(grid).Header("SDK info").BorderColor(Color.Blue));
        return 0;
    }
}
