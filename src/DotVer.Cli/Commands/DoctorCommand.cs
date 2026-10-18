using DotVer.Application;
using DotVer.Domain;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DotVer.Cli;

internal sealed class DoctorCommand : AsyncCommandBase<DoctorSettings>
{
    private readonly IInventoryService _inventoryService;
    private readonly ISelectionService _selectionService;
    private readonly IStateService _stateService;

    public DoctorCommand(IAnsiConsole console, IInventoryService inventoryService, ISelectionService selectionService, IStateService stateService)
        : base(console)
    {
        _inventoryService = inventoryService;
        _selectionService = selectionService;
        _stateService = stateService;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, DoctorSettings settings)
    {
        var inventory = await _inventoryService.ScanAsync(CancellationToken.None).ConfigureAwait(false);
        var selection = await _selectionService.ResolveAsync(CancellationToken.None).ConfigureAwait(false);
        var config = await _stateService.GetConfigAsync(CancellationToken.None).ConfigureAwait(false);

        var warnings = inventory.Any(static sdk => !sdk.CanBeUsedDirectly)
            ? new[] { "There are multi-SDK roots that serve only for detection/inspection." }
            : Array.Empty<string>();

        if (settings.Json)
        {
            global::System.Console.Out.WriteLine(JsonReports.ForDoctor(inventory, selection, config, warnings));
            return 0;
        }

        var grid = new Grid();
        grid.AddColumn();
        grid.AddColumn();
        grid.AddRow("SDKs detected", inventory.Count.ToString());
        grid.AddRow("Managed SDKs", inventory.Count(static sdk => sdk.IsManaged).ToString());
        grid.AddRow("Direct-select SDKs", inventory.Count(static sdk => sdk.CanBeUsedDirectly).ToString());
        grid.AddRow("Current", selection.Installation?.Version.Raw ?? "system");
        grid.AddRow("Default", config.DefaultVersion ?? "(unset)");
        grid.AddRow("Aliases", config.Aliases.Count.ToString());
        grid.AddRow("Warning", inventory.Any(static sdk => !sdk.CanBeUsedDirectly) ? "There are multi-SDK roots that serve only for detection/inspection." : "None");

        Console.Write(new Panel(grid).Header("Doctor").BorderColor(Color.Yellow));
        return 0;
    }
}
