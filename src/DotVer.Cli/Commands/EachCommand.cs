using Spectre.Console;
using Spectre.Console.Cli;

namespace DotVer.Cli;

/// <summary>
/// Help/discovery surface only. Real execution is handled in Program before
/// Spectre parsing so child flags are never parsed by dotver.
/// </summary>
internal sealed class EachCommand : AsyncCommandBase
{
    public EachCommand(IAnsiConsole console)
        : base(console)
    {
    }

    public override Task<int> ExecuteAsync(CommandContext context)
    {
        Console.MarkupLine("Usage: [aqua]dotver each <token[,token...]> [--parallel] [--json] -- <command> [args...][/]");
        Console.MarkupLine("Example: [aqua]dotver each 8.0,9.0,10.0 -- dotnet test[/]");
        return Task.FromResult(2);
    }
}
