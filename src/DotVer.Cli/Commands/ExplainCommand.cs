using DotVer.Application;
using DotVer.Domain;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DotVer.Cli;

internal sealed class ExplainCommand : AsyncCommandBase<ExplainSettings>
{
    private readonly ISelectionService _selectionService;

    public ExplainCommand(IAnsiConsole console, ISelectionService selectionService)
        : base(console)
    {
        _selectionService = selectionService;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, ExplainSettings settings)
    {
        var trace = await _selectionService.ExplainAsync(CancellationToken.None).ConfigureAwait(false);
        var exitCode = trace.Error is null ? 0 : 1;

        if (settings.Json)
        {
            global::System.Console.Out.WriteLine(JsonReports.ForExplain(trace));
            return exitCode;
        }

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Step");
        table.AddColumn("Value");
        table.AddColumn("Outcome");
        table.AddColumn("Detail");
        foreach (var step in trace.Steps)
        {
            table.AddRow(
                new Text(step.Source),
                new Text(TerminalText.Sanitize(step.Value ?? "-")),
                new Markup(Colorize(step)),
                new Text(TerminalText.Sanitize(step.Detail)));
        }

        Console.Write(table);

        if (trace.Error is not null)
        {
            Console.MarkupLine($"[red]Error:[/] {Markup.Escape(TerminalText.Sanitize(trace.Error))}");
            return exitCode;
        }

        var version = trace.Selection.Installation?.Version.Raw ?? "system";
        Console.MarkupLine($"Result: [green]{Markup.Escape(version)}[/] [grey]({Markup.Escape(TerminalText.Sanitize(trace.Selection.Reason))})[/]");
        return exitCode;
    }

    private static string Colorize(SelectionStep step) =>
        step.Outcome switch
        {
            SelectionOutcomes.Selected => "[green]selected[/]",
            SelectionOutcomes.Error => "[red]error[/]",
            SelectionOutcomes.Unmatched => "[yellow]unmatched[/]",
            SelectionOutcomes.Overridden => "[grey]overridden[/]",
            _ => "[grey]skipped[/]"
        };
}
