using DotVer.Application;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DotVer.Cli;

internal sealed class WhichCommand : AsyncCommandBase<WhichSettings>
{
    private readonly IExecService _execService;
    private readonly ISelectionService _selectionService;
    private readonly IInventoryService _inventoryService;

    public WhichCommand(
        IAnsiConsole console,
        IExecService execService,
        ISelectionService selectionService,
        IInventoryService inventoryService)
        : base(console)
    {
        _execService = execService;
        _selectionService = selectionService;
        _inventoryService = inventoryService;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, WhichSettings settings)
    {
        string? version;
        string? dotnetPath;
        string? hostRoot;
        string reason;

        if (!string.IsNullOrWhiteSpace(settings.Token))
        {
            var sdk = await _execService.ResolveSdkAsync(settings.Token, CancellationToken.None).ConfigureAwait(false);
            version = sdk.Version.Raw;
            dotnetPath = sdk.DotnetPath;
            hostRoot = sdk.HostRoot;
            reason = $"Resolved from token '{settings.Token}'";
        }
        else
        {
            var selection = await _selectionService.ResolveAsync(CancellationToken.None).ConfigureAwait(false);
            version = selection.Installation?.Version.Raw;
            dotnetPath = selection.Installation?.DotnetPath ?? _inventoryService.GetSystemDotnetPath();
            hostRoot = selection.Installation?.HostRoot;
            reason = selection.Reason;
        }

        if (settings.Json)
        {
            global::System.Console.Out.WriteLine(JsonReports.ForWhich(version, dotnetPath, hostRoot, reason));
        }
        else
        {
            // Plain path only: `which` output is meant for scripts.
            global::System.Console.Out.WriteLine(dotnetPath ?? "dotnet");
        }

        return 0;
    }
}
