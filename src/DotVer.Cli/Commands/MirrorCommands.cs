using DotVer.Application;
using DotVer.Domain;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DotVer.Cli;

internal sealed class MirrorShowSettings : JsonSettings;

internal sealed class MirrorSetSettings : CommandSettings
{
    [CommandOption("--install-script <URL>")]
    public string? InstallScript { get; init; }

    [CommandOption("--releases-index <URL>")]
    public string? ReleasesIndex { get; init; }
}

internal sealed class MirrorShowCommand : AsyncCommandBase<MirrorShowSettings>
{
    private readonly IDownloadSources _sources;

    public MirrorShowCommand(IAnsiConsole console, IDownloadSources sources)
        : base(console)
    {
        _sources = sources;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, MirrorShowSettings settings)
    {
        var sources = await _sources.ResolveAsync(CancellationToken.None).ConfigureAwait(false);
        if (settings.Json)
        {
            global::System.Console.Out.WriteLine(JsonReports.ForDownloadSources(sources));
            return 0;
        }

        Render(Console, sources);
        return 0;
    }

    internal static void Render(IAnsiConsole console, DownloadSources sources)
    {
        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Source");
        table.AddColumn("URL");
        table.AddRow(new Text("Install script (PowerShell)"), new Text(sources.PowerShellInstallScriptUrl));
        table.AddRow(new Text("Install script (bash)"), new Text(sources.ShellInstallScriptUrl));
        table.AddRow(new Text("Releases index"), new Text(sources.ReleasesIndexUrl));
        table.AddRow(new Text("Extra allowed hosts"), new Text(sources.ExtraAllowedHosts.Count == 0 ? "(none)" : string.Join(", ", sources.ExtraAllowedHosts)));
        console.Write(table);
        console.MarkupLine(sources.IsMirrored
            ? "[yellow]Mirror active.[/] [grey]The install script is still signature-checked on Windows; audit always uses the official index.[/]"
            : "[grey]Official Microsoft sources (no mirror configured).[/]");
    }
}

internal sealed class MirrorSetCommand : AsyncCommandBase<MirrorSetSettings>
{
    private readonly IMirrorService _mirror;
    private readonly IDownloadSources _sources;

    public MirrorSetCommand(IAnsiConsole console, IMirrorService mirror, IDownloadSources sources)
        : base(console)
    {
        _mirror = mirror;
        _sources = sources;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, MirrorSetSettings settings)
    {
        await _mirror.SetAsync(settings.InstallScript, settings.ReleasesIndex, CancellationToken.None).ConfigureAwait(false);
        var sources = await _sources.ResolveAsync(CancellationToken.None).ConfigureAwait(false);
        Console.MarkupLine("[green]Mirror saved.[/]");
        MirrorShowCommand.Render(Console, sources);
        return 0;
    }
}

internal sealed class MirrorClearCommand : AsyncCommandBase
{
    private readonly IMirrorService _mirror;

    public MirrorClearCommand(IAnsiConsole console, IMirrorService mirror)
        : base(console)
    {
        _mirror = mirror;
    }

    public override async Task<int> ExecuteAsync(CommandContext context)
    {
        var cleared = await _mirror.ClearAsync(CancellationToken.None).ConfigureAwait(false);
        Console.MarkupLine(cleared
            ? "[green]Mirror cleared;[/] downloads use the official Microsoft sources again."
            : "[grey]No mirror was configured.[/]");
        return 0;
    }
}
