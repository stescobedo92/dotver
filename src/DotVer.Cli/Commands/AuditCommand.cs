using DotVer.Application;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DotVer.Cli;

internal sealed class AuditCommand : AsyncCommandBase<AuditSettings>
{
    private readonly IAuditService _auditService;

    public AuditCommand(IAnsiConsole console, IAuditService auditService)
        : base(console)
    {
        _auditService = auditService;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, AuditSettings settings)
    {
        var entries = await _auditService.GetAuditAsync(CancellationToken.None).ConfigureAwait(false);
        var scoped = entries
            .Where(entry => !settings.ManagedOnly || entry.Installation.IsManaged)
            .ToList();
        var filtered = scoped
            .Where(entry => settings.All || entry.IsVulnerable || !entry.MetadataKnown)
            .ToList();

        // Fail closed: unknown metadata must never pass as a clean audit.
        var vulnerableCount = scoped.Count(static entry => entry.IsVulnerable);
        var unknownCount = scoped.Count(static entry => !entry.MetadataKnown);
        var exitCode = vulnerableCount > 0 ? 1 : unknownCount > 0 ? 2 : 0;

        if (settings.Json)
        {
            global::System.Console.Out.WriteLine(JsonReports.ForAudit(filtered));
            return exitCode;
        }

        Console.MarkupLine($"Audited [aqua]{scoped.Count}[/] SDK(s): [red]{vulnerableCount}[/] vulnerable, [yellow]{unknownCount}[/] unknown.");

        if (filtered.Count == 0 && unknownCount == 0)
        {
            Console.MarkupLine("[green]No known security fixes are missing from the detected SDKs.[/]");
            return exitCode;
        }

        if (unknownCount > 0)
        {
            Console.MarkupLine("[yellow]Some SDKs could not be verified against official release metadata (status unknown).[/]");
        }

        var shown = filtered;
        if (shown.Count > 0)
        {
            var table = new Table().Border(TableBorder.Rounded);
            table.AddColumn("Version");
            table.AddColumn("Channel");
            table.AddColumn("Status");
            table.AddColumn("Fixed in");
            table.AddColumn("CVEs");
            foreach (var entry in shown)
            {
                table.AddRow(
                    new Text(entry.Installation.Version.Raw),
                    new Text(entry.Channel),
                    new Text(entry.IsVulnerable ? "vulnerable" : entry.MetadataKnown ? "ok" : "unknown"),
                    new Text(entry.FixedInSdk ?? "-"),
                    new Text(entry.CveIds.Count.ToString()));
            }

            Console.Write(table);
        }

        foreach (var entry in shown.Where(static entry => entry.IsVulnerable))
        {
            Console.MarkupLine(
                $"[red]{Markup.Escape(entry.Installation.Version.Raw)}[/] fixed by [aqua]{Markup.Escape(entry.FixedInSdk ?? string.Empty)}[/]: {Markup.Escape(string.Join(", ", entry.CveIds))}");
        }

        if (vulnerableCount > 0)
        {
            Console.MarkupLine("Run [aqua]dotver upgrade --all[/] to move managed SDKs to the fixed releases.");
        }

        return exitCode;
    }
}
