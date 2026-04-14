using DotVer.Application;
using DotVer.Domain;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DotVer.Cli;

internal sealed class UninstallCommand : AsyncCommandBase<UninstallSettings>
{
    private readonly IStateService _stateService;
    private readonly IEnvironmentBootstrapService _bootstrapService;

    public UninstallCommand(IAnsiConsole console, IStateService stateService, IEnvironmentBootstrapService bootstrapService)
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
        table.AddColumn("Step");
        table.AddColumn("Value");
        table.AddRow("SDK removed", outcome.RemovedVersion);
        table.AddRow("Directory", outcome.RemovedDirectory ? "removed" : "not found");
        table.AddRow("Current", outcome.NewCurrentVersion ?? "(unset)");
        table.AddRow("Default", outcome.NewDefaultVersion ?? "(unset)");
        table.AddRow("Aliases removed", outcome.RemovedAliases.Count.ToString());
        table.AddRow("Shims", bootstrap.ShimsDirectory);
        Console.Write(table);
        return 0;
    }
}
