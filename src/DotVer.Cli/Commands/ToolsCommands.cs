using DotVer.Application;
using DotVer.Domain;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DotVer.Cli;

internal sealed class ToolsListSettings : JsonSettings;

internal sealed class ToolsAddSettings : CommandSettings
{
    [CommandArgument(0, "<ID>")]
    public string Id { get; init; } = string.Empty;

    [CommandOption("--version <VERSION>")]
    public string? Version { get; init; }
}

internal sealed class ToolsRemoveSettings : CommandSettings
{
    [CommandArgument(0, "<ID>")]
    public string Id { get; init; } = string.Empty;
}

internal sealed class ToolsSyncSettings : JsonSettings
{
    [CommandArgument(0, "[TOKEN]")]
    public string? Token { get; init; }
}

internal sealed class ToolsListCommand : AsyncCommandBase<ToolsListSettings>
{
    private readonly IToolSyncService _tools;
    private readonly IToolsManifestStore _store;

    public ToolsListCommand(IAnsiConsole console, IToolSyncService tools, IToolsManifestStore store)
        : base(console)
    {
        _tools = tools;
        _store = store;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, ToolsListSettings settings)
    {
        var manifest = await _tools.ListAsync(CancellationToken.None).ConfigureAwait(false);
        if (settings.Json)
        {
            global::System.Console.Out.WriteLine(JsonReports.ForToolsManifest(manifest, _store.FilePath));
            return 0;
        }

        if (manifest.Tools.Count == 0)
        {
            Console.MarkupLine($"[grey]No tools configured. Add one with[/] [aqua]dotver tools add <id> [[--version <v>]][/] [grey]({Markup.Escape(TerminalText.Sanitize(_store.FilePath))}).[/]");
            return 0;
        }

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Tool");
        table.AddColumn("Version");
        foreach (var tool in manifest.Tools)
        {
            table.AddRow(new Text(tool.Id), new Text(tool.Version ?? "latest"));
        }

        Console.Write(table);
        return 0;
    }
}

internal sealed class ToolsAddCommand : AsyncCommandBase<ToolsAddSettings>
{
    private readonly IToolSyncService _tools;

    public ToolsAddCommand(IAnsiConsole console, IToolSyncService tools)
        : base(console)
    {
        _tools = tools;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, ToolsAddSettings settings)
    {
        await _tools.AddAsync(settings.Id, settings.Version, CancellationToken.None).ConfigureAwait(false);
        Console.MarkupLine($"[green]Tool recorded:[/] {Markup.Escape(settings.Id.Trim())} {Markup.Escape(settings.Version is null ? "(latest)" : settings.Version.Trim())}. It is installed after every [aqua]dotver install[/]; run [aqua]dotver tools sync[/] to install it now.");
        return 0;
    }
}

internal sealed class ToolsRemoveCommand : AsyncCommandBase<ToolsRemoveSettings>
{
    private readonly IToolSyncService _tools;

    public ToolsRemoveCommand(IAnsiConsole console, IToolSyncService tools)
        : base(console)
    {
        _tools = tools;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, ToolsRemoveSettings settings)
    {
        var removed = await _tools.RemoveAsync(settings.Id, CancellationToken.None).ConfigureAwait(false);
        Console.MarkupLine(removed
            ? $"[green]Tool removed from the manifest:[/] {Markup.Escape(TerminalText.Sanitize(settings.Id.Trim()))} (the installed tool itself is untouched)."
            : $"[yellow]Not in the manifest:[/] {Markup.Escape(TerminalText.Sanitize(settings.Id.Trim()))}");
        return removed ? 0 : 1;
    }
}

internal sealed class ToolsSyncCommand : AsyncCommandBase<ToolsSyncSettings>
{
    private readonly IToolSyncService _tools;
    private readonly IExecService _execService;
    private readonly ISelectionService _selectionService;

    public ToolsSyncCommand(IAnsiConsole console, IToolSyncService tools, IExecService execService, ISelectionService selectionService)
        : base(console)
    {
        _tools = tools;
        _execService = execService;
        _selectionService = selectionService;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, ToolsSyncSettings settings)
    {
        SdkInstallation sdk;
        if (!string.IsNullOrWhiteSpace(settings.Token))
        {
            sdk = await _execService.ResolveSdkAsync(settings.Token, CancellationToken.None).ConfigureAwait(false);
        }
        else
        {
            var selection = await _selectionService.ResolveAsync(CancellationToken.None).ConfigureAwait(false);
            sdk = selection.Installation
                ?? throw new InvalidOperationException("No SDK is currently selected. Pass a token, e.g. 'dotver tools sync 8.0'.");
        }

        using var cancellation = CtrlCCancellation.Create();
        var result = await _tools.SyncAsync(sdk, cancellation.Token).ConfigureAwait(false);
        var exitCode = result.Failed > 0 ? 1 : 0;

        if (settings.Json)
        {
            global::System.Console.Out.WriteLine(JsonReports.ForToolSync(result));
            return exitCode;
        }

        Render(Console, result);
        return exitCode;
    }

    internal static void Render(IAnsiConsole console, ToolSyncResult result)
    {
        if (result.Tools.Count == 0)
        {
            console.MarkupLine("[grey]No tools configured (dotver tools add <id>).[/]");
            return;
        }

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Tool");
        table.AddColumn("Version");
        table.AddColumn("Result");
        foreach (var tool in result.Tools)
        {
            table.AddRow(
                new Text(tool.Id),
                new Text(tool.Version ?? "latest"),
                new Markup(tool.Succeeded ? "[green]synced[/]" : $"[red]failed (exit {tool.ExitCode})[/]"));
        }

        console.Write(table);
        foreach (var tool in result.Tools.Where(static tool => !tool.Succeeded && tool.Error is not null))
        {
            console.MarkupLine($"[red]{Markup.Escape(tool.Id)}:[/] {Markup.Escape(tool.Error!)}");
        }

        console.MarkupLine($"[grey]Synced with SDK {Markup.Escape(result.Sdk.Version.Raw)} ({Markup.Escape(TerminalText.Sanitize(result.Sdk.HostRoot))}).[/]");
    }
}
