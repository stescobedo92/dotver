using DotVer.Application;
using DotVer.Domain;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DotVer.Cli;

internal sealed class InstallCommand : AsyncCommandBase<InstallSettings>
{
    private readonly IInstallService _installService;
    private readonly IStateService _stateService;
    private readonly IEnvironmentBootstrapService _bootstrapService;

    public InstallCommand(
        IAnsiConsole console,
        IInstallService installService,
        IStateService stateService,
        IEnvironmentBootstrapService bootstrapService)
        : base(console)
    {
        _installService = installService;
        _stateService = stateService;
        _bootstrapService = bootstrapService;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, InstallSettings settings)
    {
        InstallPlan? plan = null;
        InstallOutcome? outcome = null;
        EnvironmentBootstrapResult? bootstrap = null;
        SdkInstallation? selected = null;

        await Console.Progress()
            .AutoClear(false)
            .StartAsync(async ctx =>
            {
                var resolveTask = ctx.AddTask("[green]Resolving version or channel[/]");
                plan = await _installService.ResolveInstallPlanAsync(settings.VersionOrChannel, CancellationToken.None).ConfigureAwait(false);
                resolveTask.Value = 100;

                var installTask = ctx.AddTask($"[green]Installing {Markup.Escape(plan.ResolvedVersion)}[/]");
                outcome = await _installService.InstallAsync(settings.VersionOrChannel, CancellationToken.None).ConfigureAwait(false);
                installTask.Value = 100;

                if (!settings.NoUse || !settings.NoDefault)
                {
                    var stateTask = ctx.AddTask("[green]Applying automatic current/default[/]");
                    var version = outcome.Installation.Version.Raw;
                    selected = (settings.NoUse, settings.NoDefault) switch
                    {
                        (false, false) => await _stateService.SetCurrentAndDefaultAsync(version, CancellationToken.None).ConfigureAwait(false),
                        (false, true) => await _stateService.SetCurrentAsync(version, CancellationToken.None).ConfigureAwait(false),
                        (true, false) => await _stateService.SetDefaultAsync(version, CancellationToken.None).ConfigureAwait(false),
                        _ => outcome.Installation
                    };
                    stateTask.Value = 100;
                }
                else
                {
                    selected = outcome.Installation;
                }

                var bootstrapTask = ctx.AddTask("[green]Configuring shims, PATH and profiles[/]");
                bootstrap = await _bootstrapService.EnsureConfiguredAsync(CancellationToken.None).ConfigureAwait(false);
                bootstrapTask.Value = 100;
            }).ConfigureAwait(false);

        var effectiveConfig = await _stateService.GetConfigAsync(CancellationToken.None).ConfigureAwait(false);

        var summary = new Table().Border(TableBorder.Rounded);
        summary.AddColumn("Step");
        summary.AddColumn("Value");
        // Text cells: AddRow(string,...) parses values as Spectre markup.
        summary.AddRow(new Text("Requested"), new Text(settings.VersionOrChannel));
        summary.AddRow(new Text("Resolved"), new Text(plan!.ResolvedVersion));
        summary.AddRow(new Text("Reason"), new Text(plan.Reason));
        summary.AddRow(new Text("Status"), new Text(outcome!.WasAlreadyInstalled ? "Already installed" : "Installed"));
        summary.AddRow(new Text("Installed SDK"), new Text(selected!.Version.Raw));
        summary.AddRow(new Text("Current"), new Text(effectiveConfig.CurrentVersion ?? "(unset)"));
        summary.AddRow(new Text("Default"), new Text(effectiveConfig.DefaultVersion ?? "(unset)"));
        summary.AddRow(new Text("Managed root"), new Text(outcome.Installation.HostRoot));
        summary.AddRow(new Text("Shims"), new Text(bootstrap!.ShimsDirectory));
        summary.AddRow("Persistent PATH", bootstrap.PersistentPathUpdated ? "updated" : "already configured");
        summary.AddRow("Process PATH", bootstrap.ProcessPathUpdated ? "updated" : "already configured");
        Console.Write(summary);

        foreach (var note in bootstrap.Notes)
        {
            Console.MarkupLine($"[grey]-[/] {Markup.Escape(note)}");
        }

        return 0;
    }
}
