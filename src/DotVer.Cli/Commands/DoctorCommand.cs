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
    private readonly IDoctorService _doctorService;

    public DoctorCommand(
        IAnsiConsole console,
        IInventoryService inventoryService,
        ISelectionService selectionService,
        IStateService stateService,
        IDoctorService doctorService)
        : base(console)
    {
        _inventoryService = inventoryService;
        _selectionService = selectionService;
        _stateService = stateService;
        _doctorService = doctorService;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, DoctorSettings settings)
    {
        var diagnosis = settings.Fix
            ? await _doctorService.FixAsync(CancellationToken.None).ConfigureAwait(false)
            : await _doctorService.DiagnoseAsync(CancellationToken.None).ConfigureAwait(false);

        var inventory = await _inventoryService.ScanAsync(CancellationToken.None).ConfigureAwait(false);
        var selection = await _selectionService.ResolveAsync(CancellationToken.None).ConfigureAwait(false);
        var config = await _stateService.GetConfigAsync(CancellationToken.None).ConfigureAwait(false);

        var warnings = diagnosis.Issues.Select(static issue => issue.Message).ToList();

        var exitCode = diagnosis.Issues.Count > 0 ? 1 : 0;

        if (settings.Json)
        {
            global::System.Console.Out.WriteLine(JsonReports.ForDoctor(inventory, selection, config, warnings, diagnosis));
            return exitCode;
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
        grid.AddRow("Issues", diagnosis.Issues.Count.ToString());

        Console.Write(new Panel(grid).Header("Doctor").BorderColor(Color.Yellow));

        foreach (var fix in diagnosis.FixesApplied)
        {
            Console.MarkupLine($"[green]Fixed:[/] {Markup.Escape(fix)}");
        }

        foreach (var issue in diagnosis.Issues)
        {
            Console.MarkupLine($"[yellow]{Markup.Escape(issue.Code)}[/] {Markup.Escape(issue.Message)}");
        }

        if (!settings.Fix && diagnosis.Issues.Count > 0)
        {
            Console.MarkupLine("Run [aqua]dotver doctor --fix[/] to repair fixable issues.");
        }

        return exitCode;
    }
}
