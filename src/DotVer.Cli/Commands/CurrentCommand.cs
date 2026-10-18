using DotVer.Application;
using DotVer.Domain;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DotVer.Cli;

internal sealed class CurrentCommand : AsyncCommandBase<CurrentSettings>
{
    private readonly ISelectionService _selectionService;

    public CurrentCommand(IAnsiConsole console, ISelectionService selectionService)
        : base(console)
    {
        _selectionService = selectionService;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, CurrentSettings settings)
    {
        var selection = await _selectionService.ResolveAsync(CancellationToken.None).ConfigureAwait(false);
        if (settings.Json)
        {
            // Raw stdout: Spectre word-wrap would corrupt JSON on narrow terminals.
            global::System.Console.Out.WriteLine(JsonReports.ForCurrent(selection));
            return 0;
        }

        Console.MarkupLine($"[green]{Markup.Escape(selection.Installation?.Version.Raw ?? "system")}[/] [grey]({Markup.Escape(selection.Reason)})[/]");
        return 0;
    }
}
