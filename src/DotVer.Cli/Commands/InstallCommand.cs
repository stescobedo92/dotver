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
        var tokens = settings.VersionsOrChannels
            .Select(static token => token.Trim())
            .Where(static token => token.Length > 0)
            .ToArray();
        if (tokens.Length == 0)
        {
            throw new InvalidOperationException("Pass at least one version or channel: dotver install 8.0 9.0 lts");
        }

        var offline = !string.IsNullOrWhiteSpace(settings.From);
        var checksum = ResolveChecksum(settings, offline);
        if (offline && !string.IsNullOrWhiteSpace(settings.Runtime))
        {
            throw new InvalidOperationException("--from installs SDK archives only; runtime archives are not supported.");
        }

        if ((offline || !string.IsNullOrWhiteSpace(settings.Runtime)) && tokens.Length > 1)
        {
            throw new InvalidOperationException("--from and --runtime take exactly one version; several versions are only supported for SDK installs from the network.");
        }

        if (!string.IsNullOrWhiteSpace(settings.Runtime))
        {
            if (settings.NoUse || settings.NoDefault)
            {
                throw new InvalidOperationException("--no-use/--no-default do not apply to runtime installs; runtimes never change selection.");
            }

            var runtimeOutcome = await _installService.InstallRuntimeAsync(
                tokens[0], settings.Runtime, settings.Architecture, CancellationToken.None).ConfigureAwait(false);
            var runtimeSummary = new Table().Border(TableBorder.Rounded);
            runtimeSummary.AddColumn("Step");
            runtimeSummary.AddColumn("Value");
            runtimeSummary.AddRow(new Text("Runtime"), new Text(runtimeOutcome.Runtime));
            runtimeSummary.AddRow(new Text("Requested"), new Text(TerminalText.Sanitize(tokens[0])));
            runtimeSummary.AddRow(new Text("Status"), new Text(runtimeOutcome.WasAlreadyInstalled ? "Already installed" : "Installed"));
            runtimeSummary.AddRow(new Text("Installed versions"), new Text(string.Join(", ", runtimeOutcome.InstalledVersions)));
            runtimeSummary.AddRow(new Text("Managed root"), new Text(TerminalText.Sanitize(runtimeOutcome.HostRoot)));
            Console.Write(runtimeSummary);
            return 0;
        }

        var options = new InstallOptions(settings.Architecture);
        MultiInstallResult? result = null;
        EnvironmentBootstrapResult? bootstrap = null;
        SdkInstallation? selected = null;
        var toolSyncs = new List<(InstallOutcome Outcome, ToolSyncResult Sync)>();
        var hooks = new List<(InstallOutcome Outcome, HookRunResult Hook)>();

        await Console.Progress()
            .AutoClear(false)
            .StartAsync(async ctx =>
            {
                if (offline)
                {
                    var archiveTask = ctx.AddTask("[green]Verifying and extracting the archive[/]");
                    var outcome = await _installService.InstallFromArchiveAsync(
                        tokens[0], settings.From!, checksum!, options, CancellationToken.None).ConfigureAwait(false);
                    result = new MultiInstallResult([new MultiInstallEntry(tokens[0], outcome.Plan, outcome, null)]);
                    archiveTask.Value = 100;
                }
                else
                {
                    var installTask = ctx.AddTask(
                        tokens.Length == 1
                            ? $"[green]Installing {Markup.Escape(tokens[0])}[/]"
                            : $"[green]Installing {tokens.Length} SDKs[/]",
                        maxValue: tokens.Length);
                    installTask.IsIndeterminate = true;
                    var progress = new InlineProgress<string>(message => installTask.Description = "[green]" + Markup.Escape(message) + "[/]");

                    result = await _installService.InstallManyAsync(tokens, options, progress, CancellationToken.None).ConfigureAwait(false);

                    installTask.IsIndeterminate = false;
                    installTask.Description = tokens.Length == 1
                        ? $"[green]Installing {Markup.Escape(tokens[0])}[/]"
                        : $"[green]Installed {result.Outcomes.Count} of {tokens.Length} SDKs[/]";
                    installTask.Value = installTask.MaxValue;
                }

                // A single failing token keeps the classic behaviour: fail loudly.
                if (tokens.Length == 1 && result.Entries[0].Error is { } singleError)
                {
                    throw new InvalidOperationException(singleError);
                }

                // The last token that produced a host-architecture SDK becomes
                // current/default; a foreign-architecture SDK is never auto-selected
                // because the host may not be able to run it without emulation.
                var candidate = result.Outcomes.LastOrDefault(outcome =>
                    Architectures.MatchesHost(outcome.Installation.Architecture, _environment.RuntimeArchitecture));
                if (candidate is not null && (!settings.NoUse || !settings.NoDefault))
                {
                    var stateTask = ctx.AddTask("[green]Applying automatic current/default[/]");
                    var version = candidate.Installation.Version.Raw;
                    selected = (settings.NoUse, settings.NoDefault) switch
                    {
                        (false, false) => await _stateService.SetCurrentAndDefaultAsync(version, CancellationToken.None).ConfigureAwait(false),
                        (false, true) => await _stateService.SetCurrentAsync(version, CancellationToken.None).ConfigureAwait(false),
                        (true, false) => await _stateService.SetDefaultAsync(version, CancellationToken.None).ConfigureAwait(false),
                        _ => candidate.Installation
                    };
                    stateTask.Value = 100;
                }
                else
                {
                    selected = (candidate ?? result.Outcomes.LastOrDefault())?.Installation;
                }

                if (result.Outcomes.Count > 0)
                {
                    var bootstrapTask = ctx.AddTask("[green]Configuring shims, PATH and profiles[/]");
                    bootstrap = await _bootstrapService.EnsureConfiguredAsync(CancellationToken.None).ConfigureAwait(false);
                    bootstrapTask.Value = 100;
                }

                // A fresh SDK gets the recorded global tools and the post-install
                // hook; a no-op re-install skips both.
                foreach (var outcome in result.Outcomes.Where(static outcome => !outcome.WasAlreadyInstalled))
                {
                    if (!settings.NoTools)
                    {
                        var toolsTask = ctx.AddTask($"[green]Syncing global tools for {Markup.Escape(outcome.Installation.Version.Raw)}[/]");
                        toolSyncs.Add((outcome, await _toolSync.SyncAsync(outcome.Installation, CancellationToken.None).ConfigureAwait(false)));
                        toolsTask.Value = 100;
                    }

                    hooks.Add((outcome, await _hooks.RunAsync("post-install", outcome.Installation, CancellationToken.None).ConfigureAwait(false)));
                }
            }).ConfigureAwait(false);

        var effectiveConfig = await _stateService.GetConfigAsync(CancellationToken.None).ConfigureAwait(false);
        var installs = result ?? throw new InvalidOperationException("The install step produced no result.");

        if (tokens.Length == 1)
        {
            Console.Write(SingleSummary(installs.Entries[0], effectiveConfig, selected!, bootstrap!, toolSyncs, hooks));
        }
        else
        {
            Console.Write(ManySummary(installs));
            Console.Write(SelectionSummary(installs, effectiveConfig, bootstrap, toolSyncs, hooks));
        }

        if (bootstrap is not null)
        {
            foreach (var note in bootstrap.Notes)
            {
                Console.MarkupLine($"[grey]-[/] {Markup.Escape(note)}");
            }
        }

        foreach (var (_, sync) in toolSyncs)
        {
            foreach (var tool in sync.Tools.Where(static tool => !tool.Succeeded))
            {
                Console.MarkupLine($"[yellow]Tool {Markup.Escape(tool.Id)} failed:[/] {Markup.Escape(tool.Error ?? "exit " + tool.ExitCode)}");
            }
        }

        foreach (var (outcome, hook) in hooks.Where(static entry => entry.Hook.Ran && entry.Hook.ExitCode != 0))
        {
            Console.MarkupLine($"[yellow]post-install hook failed for {Markup.Escape(outcome.Installation.Version.Raw)} (exit {hook.ExitCode}):[/] {Markup.Escape(TerminalText.Sanitize(hook.Output ?? string.Empty))}");
        }

        return installs.AllSucceeded ? 0 : 1;
    }

    // The classic one-SDK table: Text cells, because AddRow(string,...) parses values as Spectre markup.
    private static Table SingleSummary(
        MultiInstallEntry entry,
        DotVerConfig effectiveConfig,
        SdkInstallation selected,
        EnvironmentBootstrapResult bootstrap,
        List<(InstallOutcome Outcome, ToolSyncResult Sync)> toolSyncs,
        List<(InstallOutcome Outcome, HookRunResult Hook)> hooks)
    {
        var outcome = entry.Outcome!;
        var summary = new Table().Border(TableBorder.Rounded);
        summary.AddColumn("Step");
        summary.AddColumn("Value");
        summary.AddRow(new Text("Requested"), new Text(TerminalText.Sanitize(entry.RequestedToken)));
        summary.AddRow(new Text("Resolved"), new Text(outcome.Plan.ResolvedVersion));
        summary.AddRow(new Text("Reason"), new Text(outcome.Plan.Reason));
        summary.AddRow(new Text("Status"), new Text(outcome.WasAlreadyInstalled ? "Already installed" : "Installed"));
        summary.AddRow(new Text("Installed SDK"), new Text(selected.Version.Raw));
        summary.AddRow(new Text("Current"), new Text(TerminalText.Sanitize(effectiveConfig.CurrentVersion) is { Length: > 0 } current ? current : "(unset)"));
        summary.AddRow(new Text("Default"), new Text(TerminalText.Sanitize(effectiveConfig.DefaultVersion) is { Length: > 0 } @default ? @default : "(unset)"));
        summary.AddRow(new Text("Managed root"), new Text(TerminalText.Sanitize(outcome.Installation.HostRoot)));
        summary.AddRow(new Text("Shims"), new Text(TerminalText.Sanitize(bootstrap.ShimsDirectory)));
        summary.AddRow("Persistent PATH", bootstrap.PersistentPathUpdated ? "updated" : "already configured");
        summary.AddRow("Process PATH", bootstrap.ProcessPathUpdated ? "updated" : "already configured");
        if (toolSyncs.Count > 0)
        {
            summary.AddRow(new Text("Global tools"), new Text(DescribeTools(toolSyncs[0].Sync)));
        }

        if (hooks.Count > 0 && hooks[0].Hook.Ran)
        {
            summary.AddRow(new Text("post-install hook"), new Text(DescribeHook(hooks[0].Hook)));
        }

        return summary;
    }

    private static Table ManySummary(MultiInstallResult result)
    {
        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Requested");
        table.AddColumn("Resolved");
        table.AddColumn("Status");
        table.AddColumn("Details");
        foreach (var entry in result.Entries)
        {
            var (status, details) = entry switch
            {
                { Error: { } error } => ("[red]failed[/]", error),
                { Duplicate: true } => ("[yellow]skipped[/]", $"Same SDK as an earlier token ({entry.Plan!.ResolvedVersion})."),
                { Outcome: { WasAlreadyInstalled: true } outcome } => ("[green]already installed[/]", outcome.Installation.HostRoot),
                { Outcome: { } outcome } => ("[green]installed[/]", outcome.Installation.HostRoot),
                _ => ("[grey]unknown[/]", string.Empty)
            };

            table.AddRow(
                new Text(TerminalText.Sanitize(entry.RequestedToken)),
                new Text(entry.Plan?.ResolvedVersion ?? "-"),
                new Markup(status),
                new Text(TerminalText.Sanitize(details)));
        }

        return table;
    }

    private static Table SelectionSummary(
        MultiInstallResult result,
        DotVerConfig effectiveConfig,
        EnvironmentBootstrapResult? bootstrap,
        List<(InstallOutcome Outcome, ToolSyncResult Sync)> toolSyncs,
        List<(InstallOutcome Outcome, HookRunResult Hook)> hooks)
    {
        var summary = new Table().Border(TableBorder.Rounded);
        summary.AddColumn("Step");
        summary.AddColumn("Value");
        summary.AddRow(new Text("Installed SDKs"), new Text($"{result.Outcomes.Count} of {result.Entries.Count} requested ({result.Failed} failed)"));
        summary.AddRow(new Text("Current"), new Text(TerminalText.Sanitize(effectiveConfig.CurrentVersion) is { Length: > 0 } current ? current : "(unset)"));
        summary.AddRow(new Text("Default"), new Text(TerminalText.Sanitize(effectiveConfig.DefaultVersion) is { Length: > 0 } @default ? @default : "(unset)"));
        if (bootstrap is not null)
        {
            summary.AddRow(new Text("Shims"), new Text(TerminalText.Sanitize(bootstrap.ShimsDirectory)));
            summary.AddRow("Persistent PATH", bootstrap.PersistentPathUpdated ? "updated" : "already configured");
            summary.AddRow("Process PATH", bootstrap.ProcessPathUpdated ? "updated" : "already configured");
        }

        foreach (var (outcome, sync) in toolSyncs)
        {
            summary.AddRow(new Text($"Global tools ({outcome.Installation.Version.Raw})"), new Text(DescribeTools(sync)));
        }

        foreach (var (outcome, hook) in hooks.Where(static entry => entry.Hook.Ran))
        {
            summary.AddRow(new Text($"post-install hook ({outcome.Installation.Version.Raw})"), new Text(DescribeHook(hook)));
        }

        return summary;
    }

    private static string DescribeTools(ToolSyncResult sync) =>
        sync.Tools.Count == 0
            ? "none configured"
            : (sync.Tools.Count - sync.Failed) + " synced, " + sync.Failed + " failed";

    private static string DescribeHook(HookRunResult hook) =>
        hook.ExitCode == 0 ? "ran" : "failed (exit " + hook.ExitCode + ")";

    private static ArchiveChecksum? ResolveChecksum(InstallSettings settings, bool offline)
    {
        var hasSha256 = !string.IsNullOrWhiteSpace(settings.Sha256);
        var hasSha512 = !string.IsNullOrWhiteSpace(settings.Sha512);
        if (!offline)
        {
            return hasSha256 || hasSha512
                ? throw new InvalidOperationException("--sha256/--sha512 only apply together with --from <archive>.")
                : null;
        }

        if (hasSha256 == hasSha512)
        {
            throw new InvalidOperationException("--from requires exactly one digest: --sha256 <hex> or --sha512 <hex> (publish them next to the archive).");
        }

        return hasSha256
            ? new ArchiveChecksum("sha256", settings.Sha256!)
            : new ArchiveChecksum("sha512", settings.Sha512!);
    }
}
