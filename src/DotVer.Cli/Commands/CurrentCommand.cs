using DotVer.Application;
using DotVer.Domain;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DotVer.Cli;

internal sealed class CurrentCommand : AsyncCommandBase
{
    private readonly ISelectionService _selectionService;

    public CurrentCommand(IAnsiConsole console, ISelectionService selectionService)
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
