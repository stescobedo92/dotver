using DotVer.Application;
using DotVer.Cli;
using DotVer.Domain;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DotVer.Cli;

internal abstract class AsyncCommandBase<TSettings> : AsyncCommand<TSettings>
    where TSettings : CommandSettings
{
    protected readonly IAnsiConsole Console;

    protected AsyncCommandBase(IAnsiConsole console)
    {
        Console = console;
    }

    protected static string Flag(bool value) => value ? "[green]yes[/]" : "[red]no[/]";
}

internal abstract class AsyncCommandBase : AsyncCommand
{
    protected readonly IAnsiConsole Console;

    protected AsyncCommandBase(IAnsiConsole console)
    {
        Console = console;
    }

    protected static string Flag(bool value) => value ? "[green]yes[/]" : "[red]no[/]";
}

internal sealed class DashboardCommand : AsyncCommandBase
{
    private readonly InventoryService _inventoryService;
    private readonly SelectionService _selectionService;
    private readonly StateService _stateService;

    public DashboardCommand(
        IAnsiConsole console,
        InventoryService inventoryService,
        SelectionService selectionService,
        StateService stateService)
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

            Console.Write(new Panel(summary).Header("Estado").BorderColor(Color.Grey));

            RenderInventoryTable(inventory);

            var action = Console.Prompt(
                new SelectionPrompt<string>()
                    .Title("[green]Selecciona una accion[/]")
                    .PageSize(10)
                    .AddChoices("use", "install", "uninstall", "doctor", "refresh", "exit"));

            switch (action)
            {
                case "use":
                    var choices = inventory.Where(static sdk => sdk.CanBeUsedDirectly).Select(static sdk => sdk.Version.Raw).Distinct().ToArray();
                    if (choices.Length == 0)
                    {
                        Console.MarkupLine("[red]No hay SDKs directamente seleccionables.[/]");
                        Console.Input.ReadKey(false);
                        continue;
                    }

                    var selected = Console.Prompt(new SelectionPrompt<string>().Title("Version").AddChoices(choices));
                    await _stateService.SetCurrentAsync(selected, CancellationToken.None).ConfigureAwait(false);
                    break;
                case "install":
                    Console.MarkupLine("[yellow]Usa el comando `dotver install <version>` para una instalacion larga y trazable.[/]");
                    Console.Input.ReadKey(false);
                    break;
                case "uninstall":
                    Console.MarkupLine("[yellow]Usa el comando `dotver uninstall <version>` para limpiar una instalacion managed.[/]");
                    Console.Input.ReadKey(false);
                    break;
                case "doctor":
                    await new DoctorCommand(Console, _inventoryService, _selectionService, _stateService).ExecuteAsync(context, new DoctorSettings()).ConfigureAwait(false);
                    Console.Input.ReadKey(false);
                    break;
                case "exit":
                    return 0;
            }
        }
    }

    private void RenderInventoryTable(IReadOnlyList<SdkInstallation> inventory)
    {
        var table = new Table().Border(TableBorder.Rounded).BorderColor(Color.Grey);
        table.AddColumn("Version");
        table.AddColumn("Origen");
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

internal sealed class ListSettings : CommandSettings
{
    [CommandOption("--managed-only")]
    public bool ManagedOnly { get; init; }
}

internal sealed class ListCommand : AsyncCommandBase<ListSettings>
{
    private readonly InventoryService _inventoryService;
    private readonly SelectionService _selectionService;
    private readonly StateService _stateService;

    public ListCommand(IAnsiConsole console, InventoryService inventoryService, SelectionService selectionService, StateService stateService)
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

internal sealed class CurrentCommand : AsyncCommandBase
{
    private readonly SelectionService _selectionService;

    public CurrentCommand(IAnsiConsole console, SelectionService selectionService)
        : base(console)
    {
        _selectionService = selectionService;
    }

    public override async Task<int> ExecuteAsync(CommandContext context)
    {
        var selection = await _selectionService.ResolveAsync(CancellationToken.None).ConfigureAwait(false);
        Console.MarkupLine($"[green]{selection.Installation?.Version.Raw ?? "system"}[/] [grey]({selection.Reason})[/]");
        return 0;
    }
}

internal sealed class VersionTokenSettings : CommandSettings
{
    [CommandArgument(0, "<VERSION>")]
    public string Version { get; init; } = string.Empty;
}

internal sealed class UseCommand : AsyncCommandBase<VersionTokenSettings>
{
    private readonly StateService _stateService;

    public UseCommand(IAnsiConsole console, StateService stateService)
        : base(console)
    {
        _stateService = stateService;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, VersionTokenSettings settings)
    {
        var sdk = await _stateService.SetCurrentAsync(settings.Version, CancellationToken.None).ConfigureAwait(false);
        Console.MarkupLine($"[green]Current[/] -> [aqua]{sdk.Version.Raw}[/]");
        return 0;
    }
}

internal sealed class DefaultCommand : AsyncCommandBase<VersionTokenSettings>
{
    private readonly StateService _stateService;

    public DefaultCommand(IAnsiConsole console, StateService stateService)
        : base(console)
    {
        _stateService = stateService;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, VersionTokenSettings settings)
    {
        var sdk = await _stateService.SetDefaultAsync(settings.Version, CancellationToken.None).ConfigureAwait(false);
        Console.MarkupLine($"[green]Default[/] -> [aqua]{sdk.Version.Raw}[/]");
        return 0;
    }
}

internal sealed class AliasSettings : CommandSettings
{
    [CommandArgument(0, "[NAME]")]
    public string? Name { get; init; }

    [CommandArgument(1, "[VERSION]")]
    public string? Version { get; init; }
}

internal sealed class AliasCommand : AsyncCommandBase<AliasSettings>
{
    private readonly StateService _stateService;

    public AliasCommand(IAnsiConsole console, StateService stateService)
        : base(console)
    {
        _stateService = stateService;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, AliasSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Name) || string.IsNullOrWhiteSpace(settings.Version))
        {
            var config = await _stateService.GetConfigAsync(CancellationToken.None).ConfigureAwait(false);
            var table = new Table();
            table.AddColumn("Alias");
            table.AddColumn("Version");
            foreach (var alias in config.Aliases.OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase))
            {
                table.AddRow(alias.Key, alias.Value);
            }

            Console.Write(table);
            return 0;
        }

        await _stateService.AddAliasAsync(settings.Name, settings.Version, CancellationToken.None).ConfigureAwait(false);
        Console.MarkupLine($"[green]Alias[/] [aqua]{settings.Name}[/] -> [aqua]{settings.Version}[/]");
        return 0;
    }
}

internal sealed class UnaliasSettings : CommandSettings
{
    [CommandArgument(0, "<NAME>")]
    public string Name { get; init; } = string.Empty;
}

internal sealed class UnaliasCommand : AsyncCommandBase<UnaliasSettings>
{
    private readonly StateService _stateService;

    public UnaliasCommand(IAnsiConsole console, StateService stateService)
        : base(console)
    {
        _stateService = stateService;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, UnaliasSettings settings)
    {
        await _stateService.RemoveAliasAsync(settings.Name, CancellationToken.None).ConfigureAwait(false);
        Console.MarkupLine($"[green]Alias eliminado:[/] {settings.Name}");
        return 0;
    }
}

internal sealed class InstallSettings : CommandSettings
{
    [CommandArgument(0, "<VERSION_OR_CHANNEL>")]
    public string VersionOrChannel { get; init; } = string.Empty;
}

internal sealed class InstallCommand : AsyncCommandBase<InstallSettings>
{
    private readonly InstallService _installService;
    private readonly StateService _stateService;
    private readonly EnvironmentBootstrapService _bootstrapService;

    public InstallCommand(
        IAnsiConsole console,
        InstallService installService,
        StateService stateService,
        EnvironmentBootstrapService bootstrapService)
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
                var resolveTask = ctx.AddTask("[green]Resolviendo version o canal[/]");
                plan = await _installService.ResolveInstallPlanAsync(settings.VersionOrChannel, CancellationToken.None).ConfigureAwait(false);
                resolveTask.Value = 100;

                var installTask = ctx.AddTask($"[green]Instalando {plan.ResolvedVersion}[/]");
                outcome = await _installService.InstallAsync(settings.VersionOrChannel, CancellationToken.None).ConfigureAwait(false);
                installTask.Value = 100;

                var stateTask = ctx.AddTask("[green]Aplicando current/default automaticos[/]");
                selected = await _stateService.SetCurrentAndDefaultAsync(outcome.Installation.Version.Raw, CancellationToken.None).ConfigureAwait(false);
                stateTask.Value = 100;

                var bootstrapTask = ctx.AddTask("[green]Configurando shims, PATH y perfiles[/]");
                bootstrap = await _bootstrapService.EnsureConfiguredAsync(CancellationToken.None).ConfigureAwait(false);
                bootstrapTask.Value = 100;
            }).ConfigureAwait(false);

        var summary = new Table().Border(TableBorder.Rounded);
        summary.AddColumn("Paso");
        summary.AddColumn("Valor");
        summary.AddRow("Solicitado", settings.VersionOrChannel);
        summary.AddRow("Resuelto", plan!.ResolvedVersion);
        summary.AddRow("Motivo", plan.Reason);
        summary.AddRow("Estado", outcome!.WasAlreadyInstalled ? "Ya estaba instalado" : "Instalado");
        summary.AddRow("Current", selected!.Version.Raw);
        summary.AddRow("Default", selected.Version.Raw);
        summary.AddRow("Managed root", outcome.Installation.HostRoot);
        summary.AddRow("Shims", bootstrap!.ShimsDirectory);
        summary.AddRow("PATH persistente", bootstrap.PersistentPathUpdated ? "actualizado" : "ya estaba listo");
        summary.AddRow("PATH proceso", bootstrap.ProcessPathUpdated ? "actualizado" : "ya estaba listo");
        Console.Write(summary);

        foreach (var note in bootstrap.Notes)
        {
            Console.MarkupLine($"[grey]-[/] {note}");
        }

        return 0;
    }
}

internal sealed class ListRemoteCommand : AsyncCommandBase
{
    private readonly InstallService _installService;

    public ListRemoteCommand(IAnsiConsole console, InstallService installService)
        : base(console)
    {
        _installService = installService;
    }

    public override async Task<int> ExecuteAsync(CommandContext context)
    {
        var channels = await _installService.ListRemoteAsync(CancellationToken.None).ConfigureAwait(false);
        var table = new Table();
        table.AddColumn("Channel");
        table.AddColumn("Latest SDK");
        table.AddColumn("Support");
        table.AddColumn("Type");
        foreach (var channel in channels)
        {
            table.AddRow(channel.Channel, channel.LatestSdkVersion, channel.SupportPhase, channel.ReleaseType);
        }

        Console.Write(table);
        return 0;
    }
}

internal sealed class DoctorSettings : CommandSettings;

internal sealed class DoctorCommand : AsyncCommandBase<DoctorSettings>
{
    private readonly InventoryService _inventoryService;
    private readonly SelectionService _selectionService;
    private readonly StateService _stateService;

    public DoctorCommand(IAnsiConsole console, InventoryService inventoryService, SelectionService selectionService, StateService stateService)
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

        var grid = new Grid();
        grid.AddColumn();
        grid.AddColumn();
        grid.AddRow("SDKs detectados", inventory.Count.ToString());
        grid.AddRow("SDKs managed", inventory.Count(static sdk => sdk.IsManaged).ToString());
        grid.AddRow("SDKs direct-select", inventory.Count(static sdk => sdk.CanBeUsedDirectly).ToString());
        grid.AddRow("Current", selection.Installation?.Version.Raw ?? "system");
        grid.AddRow("Default", config.DefaultVersion ?? "(unset)");
        grid.AddRow("Aliases", config.Aliases.Count.ToString());
        grid.AddRow("Advertencia", inventory.Any(static sdk => !sdk.CanBeUsedDirectly) ? "Hay roots multi-SDK que solo sirven para deteccion/inspeccion." : "Ninguna");

        Console.Write(new Panel(grid).Header("Doctor").BorderColor(Color.Yellow));
        return 0;
    }
}

internal sealed class UninstallSettings : CommandSettings
{
    [CommandArgument(0, "<VERSION>")]
    public string Version { get; init; } = string.Empty;
}

internal sealed class UninstallCommand : AsyncCommandBase<UninstallSettings>
{
    private readonly StateService _stateService;
    private readonly EnvironmentBootstrapService _bootstrapService;

    public UninstallCommand(IAnsiConsole console, StateService stateService, EnvironmentBootstrapService bootstrapService)
        : base(console)
    {
        _stateService = stateService;
        _bootstrapService = bootstrapService;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, UninstallSettings settings)
    {
        var outcome = await _stateService.UninstallManagedAsync(settings.Version, CancellationToken.None).ConfigureAwait(false);
        var bootstrap = await _bootstrapService.EnsureConfiguredAsync(CancellationToken.None).ConfigureAwait(false);

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Paso");
        table.AddColumn("Valor");
        table.AddRow("SDK removido", outcome.RemovedVersion);
        table.AddRow("Directorio", outcome.RemovedDirectory ? "eliminado" : "no encontrado");
        table.AddRow("Current", outcome.NewCurrentVersion ?? "(unset)");
        table.AddRow("Default", outcome.NewDefaultVersion ?? "(unset)");
        table.AddRow("Aliases removidos", outcome.RemovedAliases.Count.ToString());
        table.AddRow("Shims", bootstrap.ShimsDirectory);
        Console.Write(table);
        return 0;
    }
}

internal sealed class RegisterRootSettings : CommandSettings
{
    [CommandArgument(0, "<PATH>")]
    public string Path { get; init; } = string.Empty;
}

internal sealed class RegisterRootCommand : AsyncCommandBase<RegisterRootSettings>
{
    private readonly StateService _stateService;

    public RegisterRootCommand(IAnsiConsole console, StateService stateService)
        : base(console)
    {
        _stateService = stateService;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, RegisterRootSettings settings)
    {
        await _stateService.RegisterRootAsync(settings.Path, CancellationToken.None).ConfigureAwait(false);
        Console.MarkupLine($"[green]Root registrado:[/] {settings.Path}");
        return 0;
    }
}
