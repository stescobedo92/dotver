using DotVer.Application;
using DotVer.Domain;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DotVer.Cli;

internal sealed class ListRemoteCommand : AsyncCommandBase<ListRemoteSettings>
{
    private readonly IInstallService _installService;

    public ListRemoteCommand(IAnsiConsole console, IInstallService installService)
        : base(console)
    {
        _installService = installService;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, ListRemoteSettings settings)
    {
        var channels = await _installService.ListRemoteAsync(CancellationToken.None).ConfigureAwait(false);
        if (settings.Json)
        {
            global::System.Console.Out.WriteLine(JsonReports.ForRemote(channels));
            return 0;
        }

        var table = new Table();
        table.AddColumn("Channel");
        table.AddColumn("Latest SDK");
        table.AddColumn("Support");
        table.AddColumn("Type");
        foreach (var channel in channels)
        {
            table.AddRow(channel.Channel, channel.LatestSdkVersion, channel.SupportPhase, channel.ReleaseType);
        }

        Console.Write(table);
        return 0;
    }
}
