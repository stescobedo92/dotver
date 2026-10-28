using DotVer.Application;
using DotVer.Domain;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DotVer.Cli;

internal sealed class SelfUpdateSettings : JsonSettings
{
    [CommandOption("--check")]
    public bool Check { get; init; }

    [CommandOption("--version <VERSION>")]
    public string? Version { get; init; }
}

internal sealed class SelfUninstallSettings : JsonSettings
{
    [CommandOption("--yes")]
    public bool Yes { get; init; }

    [CommandOption("--keep-sdks")]
    public bool KeepSdks { get; init; }
}

internal sealed class SelfUpdateCommand : AsyncCommandBase<SelfUpdateSettings>
{
    private readonly ISelfUpdateService _selfUpdate;

    public SelfUpdateCommand(IAnsiConsole console, ISelfUpdateService selfUpdate)
        : base(console)
    {
        _selfUpdate = selfUpdate;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, SelfUpdateSettings settings)
    {
        using var cancellation = CtrlCCancellation.Create();
        if (settings.Check)
        {
            var check = await _selfUpdate.CheckAsync(settings.Version, cancellation.Token).ConfigureAwait(false);
            if (settings.Json)
            {
                global::System.Console.Out.WriteLine(JsonReports.ForSelfUpdateCheck(check));
                return 0;
            }

            Console.MarkupLine($"Installed: [aqua]{Markup.Escape(check.CurrentVersion)}[/]  Latest: [aqua]{Markup.Escape(check.LatestVersion)}[/] ({Markup.Escape(check.Tag)})");
            Console.MarkupLine(check.UpdateAvailable
                ? "[yellow]An update is available.[/] Run [aqua]dotver self-update[/] to install it."
                : "[green]dotver is up to date.[/]");
            if (!check.CanSelfUpdate)
            {
                Console.MarkupLine($"[grey]{Markup.Escape(TerminalText.Sanitize(check.Reason ?? string.Empty))}[/]");
            }

            return 0;
        }

        var outcome = await _selfUpdate.UpdateAsync(settings.Version, cancellation.Token).ConfigureAwait(false);
        if (settings.Json)
        {
            global::System.Console.Out.WriteLine(JsonReports.ForSelfUpdate(outcome));
            return 0;
        }

        if (!outcome.Updated)
        {
            Console.MarkupLine($"[green]{Markup.Escape(outcome.Note ?? "Nothing to do.")}[/] ({Markup.Escape(outcome.FromVersion)})");
            return 0;
        }

        Console.MarkupLine($"[green]Updated[/] {Markup.Escape(outcome.FromVersion)} -> [aqua]{Markup.Escape(outcome.ToVersion)}[/]");
        Console.MarkupLine($"  {Markup.Escape(TerminalText.Sanitize(outcome.ExecutablePath))}  [grey]({Markup.Escape(outcome.AssetName)}, sha256 {Markup.Escape(outcome.Sha256 ?? "n/a")})[/]");
        if (outcome.Note is not null)
        {
            Console.MarkupLine($"[grey]{Markup.Escape(TerminalText.Sanitize(outcome.Note))}[/]");
        }

        return 0;
    }
}

internal sealed class SelfUninstallCommand : AsyncCommandBase<SelfUninstallSettings>
{
    private readonly ISelfUninstallService _selfUninstall;

    public SelfUninstallCommand(IAnsiConsole console, ISelfUninstallService selfUninstall)
        : base(console)
    {
        _selfUninstall = selfUninstall;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, SelfUninstallSettings settings)
    {
        if (!settings.Yes)
        {
            var plan = await _selfUninstall.PlanAsync(settings.KeepSdks, CancellationToken.None).ConfigureAwait(false);
            if (settings.Json)
            {
                global::System.Console.Out.WriteLine(JsonReports.ForSelfUninstall(new SelfUninstallOutcome(plan, Executed: false, null, [], [])));
                return 0;
            }

            RenderPlan(plan);
            Console.MarkupLine("[yellow]Dry run.[/] Re-run with [aqua]--yes[/] to apply" + (settings.KeepSdks ? " (SDKs under installs/ and runtimes/ are kept)." : ", or add [aqua]--keep-sdks[/] to keep the downloaded SDKs."));
            return 0;
        }

        var outcome = await _selfUninstall.UninstallAsync(settings.KeepSdks, CancellationToken.None).ConfigureAwait(false);
        var exitCode = outcome.Errors.Count > 0 ? 1 : 0;
        if (settings.Json)
        {
            global::System.Console.Out.WriteLine(JsonReports.ForSelfUninstall(outcome));
            return exitCode;
        }

        foreach (var profile in outcome.Environment?.ProfilesCleaned ?? [])
        {
            Console.MarkupLine($"[green]Cleaned[/] {Markup.Escape(TerminalText.Sanitize(profile))}");
        }

        if (outcome.Environment?.UserPathUpdated == true)
        {
            Console.MarkupLine("[green]Removed[/] the shims entry from the user PATH");
        }

        if (outcome.Environment?.UserVariablesRemoved == true)
        {
            Console.MarkupLine("[green]Removed[/] DOTVER_HOME from the user environment");
        }

        foreach (var path in outcome.Deleted)
        {
            Console.MarkupLine($"[green]Deleted[/] {Markup.Escape(TerminalText.Sanitize(path))}");
        }

        foreach (var error in outcome.Errors)
        {
            Console.MarkupLine($"[red]Error:[/] {Markup.Escape(error)}");
        }

        foreach (var note in outcome.Environment?.Notes ?? [])
        {
            Console.MarkupLine($"[grey]-[/] {Markup.Escape(TerminalText.Sanitize(note))}");
        }

        Console.MarkupLine($"[yellow]Last step:[/] {Markup.Escape(TerminalText.Sanitize(outcome.Plan.BinaryRemovalHint))}");
        return exitCode;
    }

    private void RenderPlan(SelfUninstallPlan plan)
    {
        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Action");
        table.AddColumn("Target");
        foreach (var profile in plan.ProfilesToClean)
        {
            table.AddRow(new Text("remove dotver block"), new Text(TerminalText.Sanitize(profile)));
        }

        if (plan.RemoveUserPathEntry)
        {
            table.AddRow(new Text("remove from user PATH"), new Text("~/.dotver/shims entry"));
        }

        if (plan.RemoveUserVariables)
        {
            table.AddRow(new Text("remove user variable"), new Text("DOTVER_HOME"));
        }

        foreach (var directory in plan.DirectoriesToDelete)
        {
            table.AddRow(new Text("delete directory"), new Text(TerminalText.Sanitize(directory)));
        }

        foreach (var file in plan.FilesToDelete)
        {
            table.AddRow(new Text("delete file"), new Text(TerminalText.Sanitize(file)));
        }

        table.AddRow(new Text("binary (manual)"), new Text(TerminalText.Sanitize(plan.BinaryRemovalHint)));
        Console.Write(table);
    }
}
