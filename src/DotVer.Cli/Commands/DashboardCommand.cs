using DotVer.Application;
using DotVer.Domain;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DotVer.Cli;

internal sealed class DashboardCommand : AsyncCommandBase
{
    private readonly IInventoryService _inventoryService;
    private readonly ISelectionService _selectionService;
    private readonly IStateService _stateService;

    public DashboardCommand(
        IAnsiConsole console,
        IInventoryService inventoryService,
        ISelectionService selectionService,
        IStateService stateService)
        : base(console)
    {
        _inventoryService = inventoryService;
        _selectionService = selectionService;
        _stateService = stateService;
    }

    public override async Task<int> ExecuteAsync(CommandContext context)
    {
        while (true)
        {
            Console.Clear();

            var inventory = await _inventoryService.ScanAsync(CancellationToken.None).ConfigureAwait(false);
            var selection = await _selectionService.ResolveAsync(CancellationToken.None).ConfigureAwait(false);
            var config = await _stateService.GetConfigAsync(CancellationToken.None).ConfigureAwait(false);

            Console.Write(
                new FigletText("dotver")
                    .Color(Color.SpringGreen2));

            var summary = new Grid();
            summary.AddColumn();
            summary.AddColumn();
            summary.AddRow("[grey]Current[/]", $"[aqua]{selection.Installation?.Version.Raw ?? "system"}[/]");
            summary.AddRow("[grey]Default[/]", $"[aqua]{config.DefaultVersion ?? "(unset)"}[/]");
            summary.AddRow("[grey]Reason[/]", selection.Reason);
            summary.AddRow("[grey]Managed[/]", inventory.Count(static sdk => sdk.IsManaged).ToString());

            Console.Write(new Panel(summary).Header("Status").BorderColor(Color.Grey));

            RenderInventoryTable(inventory);

            var action = Console.Prompt(
                new SelectionPrompt<string>()
                    .Title("[green]Select an action[/]")
                    .PageSize(10)
                    .AddChoices("use", "install", "uninstall", "doctor", "refresh", "exit"));

            switch (action)
            {
                case "use":
                    var choices = inventory.Where(static sdk => sdk.CanBeUsedDirectly).Select(static sdk => sdk.Version.Raw).Distinct().ToArray();
                    if (choices.Length == 0)
                    {
                        Console.MarkupLine("[red]No directly selectable SDKs available.[/]");
                        Console.Input.ReadKey(false);
                        continue;
                    }

                    var selected = Console.Prompt(new SelectionPrompt<string>().Title("Version").AddChoices(choices));
                    await _stateService.SetCurrentAsync(selected, CancellationToken.None).ConfigureAwait(false);
                    break;
                case "install":
                    Console.MarkupLine("[yellow]Use the `dotver install <version>` command for a full traceable installation.[/]");
                    Console.Input.ReadKey(false);
                    break;
                case "uninstall":
                    Console.MarkupLine("[yellow]Use the `dotver uninstall <version>` command to clean up a managed installation.[/]");
                    Console.Input.ReadKey(false);
                    break;
                case "doctor":
                    await RenderDoctorPanelAsync().ConfigureAwait(false);
                    Console.Input.ReadKey(false);
                    break;
                case "exit":
                    return 0;
            }
        }
    }

    private async Task RenderDoctorPanelAsync()
    {
        var inventory = await _inventoryService.ScanAsync(CancellationToken.None).ConfigureAwait(false);
        var selection = await _selectionService.ResolveAsync(CancellationToken.None).ConfigureAwait(false);
        var config = await _stateService.GetConfigAsync(CancellationToken.None).ConfigureAwait(false);

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
    }

    private void RenderInventoryTable(IReadOnlyList<SdkInstallation> inventory)
    {
        var table = new Table().Border(TableBorder.Rounded).BorderColor(Color.Grey);
        table.AddColumn("Version");
        table.AddColumn("Origin");
        table.AddColumn("Selectable");
        table.AddColumn("Host");

        foreach (var sdk in inventory)
        {
            table.AddRow(
                sdk.Version.Raw,
                sdk.Origin.ToString(),
                sdk.CanBeUsedDirectly ? "[green]yes[/]" : "[yellow]inspect-only[/]",
                sdk.HostRoot);
        }

        Console.Write(table);
    }
}
