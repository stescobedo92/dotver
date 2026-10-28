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
    private readonly IEnvironmentContext _environment;
    private readonly IToolSyncService _toolSync;
    private readonly IHookRunner _hooks;

    public InstallCommand(
        IAnsiConsole console,
        IInstallService installService,
        IStateService stateService,
        IEnvironmentBootstrapService bootstrapService,
        IEnvironmentContext environment,
        IToolSyncService toolSync,
        IHookRunner hooks)
        : base(console)
    {
        _installService = installService;
        _stateService = stateService;
        _bootstrapService = bootstrapService;
        _environment = environment;
        _toolSync = toolSync;
        _hooks = hooks;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, InstallSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.Runtime))
        {
            if (settings.NoUse || settings.NoDefault)
            {
                throw new InvalidOperationException("--no-use/--no-default do not apply to runtime installs; runtimes never change selection.");
            }

            var runtimeOutcome = await _installService.InstallRuntimeAsync(
                settings.VersionOrChannel, settings.Runtime, settings.Architecture, CancellationToken.None).ConfigureAwait(false);
            var runtimeSummary = new Table().Border(TableBorder.Rounded);
            runtimeSummary.AddColumn("Step");
            runtimeSummary.AddColumn("Value");
            runtimeSummary.AddRow(new Text("Runtime"), new Text(runtimeOutcome.Runtime));
            runtimeSummary.AddRow(new Text("Requested"), new Text(TerminalText.Sanitize(settings.VersionOrChannel)));
            runtimeSummary.AddRow(new Text("Status"), new Text(runtimeOutcome.WasAlreadyInstalled ? "Already installed" : "Installed"));
            runtimeSummary.AddRow(new Text("Installed versions"), new Text(string.Join(", ", runtimeOutcome.InstalledVersions)));
            runtimeSummary.AddRow(new Text("Managed root"), new Text(TerminalText.Sanitize(runtimeOutcome.HostRoot)));
            Console.Write(runtimeSummary);
            return 0;
        }

        InstallPlan? plan = null;
        InstallOutcome? outcome = null;
        EnvironmentBootstrapResult? bootstrap = null;
        SdkInstallation? selected = null;
        ToolSyncResult? toolSync = null;
        HookRunResult? hook = null;

        await Console.Progress()
            .AutoClear(false)
            .StartAsync(async ctx =>
            {
                var resolveTask = ctx.AddTask("[green]Resolving version or channel[/]");
                plan = await _installService.ResolveInstallPlanAsync(settings.VersionOrChannel, CancellationToken.None).ConfigureAwait(false);
                resolveTask.Value = 100;

                var installTask = ctx.AddTask($"[green]Installing {Markup.Escape(plan.ResolvedVersion)}[/]");
                outcome = await _installService.InstallAsync(
                    settings.VersionOrChannel, new InstallOptions(settings.Architecture), CancellationToken.None).ConfigureAwait(false);
                installTask.Value = 100;

                // A foreign-architecture SDK is never auto-selected: the host
                // may not be able to run it without emulation.
                var foreignArchitecture = !Architectures.MatchesHost(outcome.Installation.Architecture, _environment.RuntimeArchitecture);
                if (foreignArchitecture)
                {
                    selected = outcome.Installation;
                }
                else if (!settings.NoUse || !settings.NoDefault)
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

                // A fresh SDK gets the recorded global tools and the post-install
                // hook; a no-op re-install skips both.
                if (!outcome.WasAlreadyInstalled)
                {
                    if (!settings.NoTools)
                    {
                        var toolsTask = ctx.AddTask("[green]Syncing global tools[/]");
                        toolSync = await _toolSync.SyncAsync(outcome.Installation, CancellationToken.None).ConfigureAwait(false);
                        toolsTask.Value = 100;
                    }

                    hook = await _hooks.RunAsync("post-install", outcome.Installation, CancellationToken.None).ConfigureAwait(false);
                }
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
        summary.AddRow(new Text("Current"), new Text(TerminalText.Sanitize(effectiveConfig.CurrentVersion) is { Length: > 0 } current ? current : "(unset)"));
        summary.AddRow(new Text("Default"), new Text(TerminalText.Sanitize(effectiveConfig.DefaultVersion) is { Length: > 0 } @default ? @default : "(unset)"));
        summary.AddRow(new Text("Managed root"), new Text(TerminalText.Sanitize(outcome.Installation.HostRoot)));
        summary.AddRow(new Text("Shims"), new Text(TerminalText.Sanitize(bootstrap!.ShimsDirectory)));
        summary.AddRow("Persistent PATH", bootstrap.PersistentPathUpdated ? "updated" : "already configured");
        summary.AddRow("Process PATH", bootstrap.ProcessPathUpdated ? "updated" : "already configured");
        if (toolSync is not null)
        {
            var toolsSummary = toolSync.Tools.Count == 0
                ? "none configured"
                : (toolSync.Tools.Count - toolSync.Failed) + " synced, " + toolSync.Failed + " failed";
            summary.AddRow(new Text("Global tools"), new Text(toolsSummary));
        }

        if (hook is not null && hook.Ran)
        {
            summary.AddRow(new Text("post-install hook"), new Text(hook.ExitCode == 0 ? "ran" : "failed (exit " + hook.ExitCode + ")"));
        }

        Console.Write(summary);

        foreach (var note in bootstrap.Notes)
        {
            Console.MarkupLine($"[grey]-[/] {Markup.Escape(note)}");
        }

        if (toolSync is not null)
        {
            foreach (var tool in toolSync.Tools.Where(static tool => !tool.Succeeded))
            {
                Console.MarkupLine($"[yellow]Tool {Markup.Escape(tool.Id)} failed:[/] {Markup.Escape(tool.Error ?? "exit " + tool.ExitCode)}");
            }
        }

        if (hook is not null && hook.Ran && hook.ExitCode != 0)
        {
            Console.MarkupLine($"[yellow]post-install hook failed (exit {hook.ExitCode}):[/] {Markup.Escape(TerminalText.Sanitize(hook.Output ?? string.Empty))}");
        }

        return 0;
    }
}
