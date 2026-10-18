using Spectre.Console;
using Spectre.Console.Cli;

namespace DotVer.Cli;

/// <summary>
/// Help/discovery surface only. Real execution is handled in Program before
/// Spectre parsing so child flags (build -c Release, etc.) are never parsed by dotver.
/// </summary>
internal sealed class ExecCommand : AsyncCommandBase
{
    public ExecCommand(IAnsiConsole console)
        : base(console)
    {
    }

    public override Task<int> ExecuteAsync(CommandContext context)
    {
        Console.MarkupLine("Usage: [aqua]dotver exec <version> -- <command> [args...][/]");
        Console.MarkupLine("Example: [aqua]dotver exec 8.0 -- dotnet test[/]");
        return Task.FromResult(2);
    }
}
