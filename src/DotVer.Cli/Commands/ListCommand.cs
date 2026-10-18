using DotVer.Application;
using DotVer.Domain;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DotVer.Cli;

internal sealed class ListCommand : AsyncCommandBase<ListSettings>
{
    private readonly IInventoryService _inventoryService;
    private readonly ISelectionService _selectionService;
    private readonly IStateService _stateService;

    public ListCommand(IAnsiConsole console, IInventoryService inventoryService, ISelectionService selectionService, IStateService stateService)
        : base(console)
    {
        _inventoryService = inventoryService;
        _selectionService = selectionService;
        _stateService = stateService;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, ListSettings settings)
    {
        var inventory = await _inventoryService.ScanAsync(CancellationToken.None).ConfigureAwait(false);
        var selection = await _selectionService.ResolveAsync(CancellationToken.None).ConfigureAwait(false);
        var config = await _stateService.GetConfigAsync(CancellationToken.None).ConfigureAwait(false);

        var filtered = settings.ManagedOnly ? inventory.Where(static sdk => sdk.IsManaged).ToList() : inventory;

        if (settings.Json)
        {
            global::System.Console.Out.WriteLine(JsonReports.ForList(filtered, selection, config));
            return 0;
        }

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Version");
        table.AddColumn("Current");
        table.AddColumn("Default");
        table.AddColumn("Managed");
        table.AddColumn("Direct");
        table.AddColumn("Origin");
        table.AddColumn("SdkRoot");

        foreach (var sdk in filtered)
        {
            table.AddRow(
                sdk.Version.Raw,
                Flag(selection.Installation?.Version.Raw == sdk.Version.Raw),
                Flag(string.Equals(config.DefaultVersion, sdk.Version.Raw, StringComparison.OrdinalIgnoreCase)),
                Flag(sdk.IsManaged),
                Flag(sdk.CanBeUsedDirectly),
                sdk.Origin.ToString(),
                sdk.SdkRoot);
        }

        Console.Write(table);
        return 0;
    }
}
