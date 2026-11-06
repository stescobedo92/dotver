using DotVer.Application;
using DotVer.Domain;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DotVer.Cli;

internal sealed class DownloadCommand : AsyncCommandBase<DownloadSettings>
{
    private readonly ISdkDownloadService _downloads;
    private readonly IEnvironmentContext _environment;

    public DownloadCommand(IAnsiConsole console, ISdkDownloadService downloads, IEnvironmentContext environment)
        : base(console)
    {
        _downloads = downloads;
        _environment = environment;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, DownloadSettings settings)
    {
        var tokens = settings.VersionsOrChannels
            .Select(static token => token.Trim())
            .Where(static token => token.Length > 0)
            .ToArray();
        if (tokens.Length == 0)
        {
            throw new InvalidOperationException("Pass at least one version or channel: dotver download 8.0 9.0 --rid linux-x64");
        }

        // No --rid means "what dotnet.exe would run here", derived like the host does.
        var rids = settings.Rids
            .Select(static rid => rid.Trim())
            .Where(static rid => rid.Length > 0)
            .ToArray();
        if (rids.Length == 0)
        {
            rids = [HostPlatformResolver.Resolve(_environment).Rid];
        }

        var outputDirectory = string.IsNullOrWhiteSpace(settings.Out)
            ? Path.Combine(_environment.CurrentDirectory, "dotnet-sdk-archives")
            : settings.Out.Trim();

        using var cancellation = CtrlCCancellation.Create();
        DownloadBatchResult result;
        if (settings.DryRun)
        {
            result = await _downloads.PlanAsync(tokens, rids, outputDirectory, cancellation.Token).ConfigureAwait(false);
        }
        else if (settings.Json)
        {
            result = await _downloads.DownloadAsync(tokens, rids, outputDirectory, progress: null, cancellation.Token).ConfigureAwait(false);
        }
        else
        {
            result = await DownloadWithProgressAsync(tokens, rids, outputDirectory, cancellation.Token).ConfigureAwait(false);
        }

        if (settings.Json)
        {
            global::System.Console.Out.WriteLine(JsonReports.ForDownload(result, settings.DryRun));
            return result.AllSucceeded ? 0 : 1;
        }

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Requested");
        table.AddColumn("RID");
        table.AddColumn("SDK");
        table.AddColumn("Archive");
        table.AddColumn("Status");
        table.AddColumn("Details");
        foreach (var entry in result.Entries)
        {
            var (status, details) = Describe(entry, settings.DryRun);
            table.AddRow(
                new Text(TerminalText.Sanitize(entry.RequestedToken)),
                new Text(entry.Rid),
                new Text(entry.ResolvedVersion ?? "-"),
                new Text(entry.Archive?.FileName ?? "-"),
                new Markup(status),
                new Text(TerminalText.Sanitize(details)));
        }

        Console.Write(table);
        Console.MarkupLine($"[grey]Output directory:[/] {Markup.Escape(TerminalText.Sanitize(result.OutputDirectory))}");

        // Each verified archive is ready for an offline install on any machine.
        foreach (var entry in result.Entries.Where(static entry => entry.Downloaded))
        {
            Console.MarkupLine(
                $"[grey]-[/] dotver install {Markup.Escape(entry.ResolvedVersion!)} --from \"{Markup.Escape(TerminalText.Sanitize(entry.Download!.Path))}\" --sha512 {Markup.Escape(entry.Archive!.Sha512!)}");
        }

        return result.AllSucceeded ? 0 : 1;
    }

    private async Task<DownloadBatchResult> DownloadWithProgressAsync(string[] tokens, string[] rids, string outputDirectory, CancellationToken cancellationToken)
    {
        DownloadBatchResult? result = null;
        await Console.Progress()
            .AutoClear(false)
            .Columns(new TaskDescriptionColumn(), new ProgressBarColumn(), new PercentageColumn(), new DownloadedColumn(), new TransferSpeedColumn())
            .StartAsync(async ctx =>
            {
                var tasks = new Dictionary<string, ProgressTask>(StringComparer.Ordinal);
                var progress = new InlineProgress<DownloadProgress>(update =>
                {
                    lock (tasks)
                    {
                        if (!tasks.TryGetValue(update.FileName, out var task))
                        {
                            task = ctx.AddTask("[green]" + Markup.Escape(update.FileName) + "[/]", maxValue: Math.Max(1, update.TotalBytes ?? update.BytesReceived));
                            tasks[update.FileName] = task;
                        }

                        if (update.TotalBytes is > 0)
                        {
                            task.MaxValue = update.TotalBytes.Value;
                        }
                        else if (update.BytesReceived > task.MaxValue)
                        {
                            task.MaxValue = update.BytesReceived;
                        }

                        task.Value = update.BytesReceived;
                    }
                });

                result = await _downloads.DownloadAsync(tokens, rids, outputDirectory, progress, cancellationToken).ConfigureAwait(false);
                lock (tasks)
                {
                    foreach (var task in tasks.Values)
                    {
                        task.StopTask();
                    }
                }
            }).ConfigureAwait(false);

        return result!;
    }

    private static (string Status, string Details) Describe(DownloadBatchEntry entry, bool dryRun)
    {
        if (entry.Error is not null)
        {
            return ("[red]failed[/]", entry.Error);
        }

        if (dryRun || entry.Download is null)
        {
            return ("[yellow]planned[/]", entry.Archive!.Url);
        }

        return entry.Download.SkippedExisting
            ? ("[green]already present[/]", entry.Download.Path)
            : ("[green]downloaded[/]", $"{entry.Download.Bytes:N0} bytes -> {entry.Download.Path}");
    }
}
