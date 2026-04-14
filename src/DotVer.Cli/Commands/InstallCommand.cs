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

                var installTask = ctx.AddTask($"[green]Installing {plan.ResolvedVersion}[/]");
                outcome = await _installService.InstallAsync(settings.VersionOrChannel, CancellationToken.None).ConfigureAwait(false);
                installTask.Value = 100;

                var stateTask = ctx.AddTask("[green]Applying automatic current/default[/]");
                selected = await _stateService.SetCurrentAndDefaultAsync(outcome.Installation.Version.Raw, CancellationToken.None).ConfigureAwait(false);
                stateTask.Value = 100;

                var bootstrapTask = ctx.AddTask("[green]Configuring shims, PATH and profiles[/]");
                bootstrap = await _bootstrapService.EnsureConfiguredAsync(CancellationToken.None).ConfigureAwait(false);
                bootstrapTask.Value = 100;
            }).ConfigureAwait(false);

        var summary = new Table().Border(TableBorder.Rounded);
        summary.AddColumn("Step");
        summary.AddColumn("Value");
        summary.AddRow("Requested", settings.VersionOrChannel);
        summary.AddRow("Resolved", plan!.ResolvedVersion);
        summary.AddRow("Reason", plan.Reason);
        summary.AddRow("Status", outcome!.WasAlreadyInstalled ? "Already installed" : "Installed");
        summary.AddRow("Current", selected!.Version.Raw);
        summary.AddRow("Default", selected.Version.Raw);
        summary.AddRow("Managed root", outcome.Installation.HostRoot);
        summary.AddRow("Shims", bootstrap!.ShimsDirectory);
        summary.AddRow("Persistent PATH", bootstrap.PersistentPathUpdated ? "updated" : "already configured");
        summary.AddRow("Process PATH", bootstrap.ProcessPathUpdated ? "updated" : "already configured");
        Console.Write(summary);

        foreach (var note in bootstrap.Notes)
        {
            Console.MarkupLine($"[grey]-[/] {note}");
        }

        return 0;
    }
}
