using DotVer.Application;
using DotVer.Domain;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DotVer.Cli;

internal sealed class CompletionsCommand : AsyncCommandBase<CompletionsSettings>
{
    public CompletionsCommand(IAnsiConsole console)
        : base(console)
    {
    }

    public override Task<int> ExecuteAsync(CommandContext context, CompletionsSettings settings)
    {
        var shell = settings.Shell.Trim().ToLowerInvariant() switch
        {
            "powershell" or "pwsh" => ShellKind.PowerShell,
            "bash" => ShellKind.Bash,
            "zsh" => ShellKind.Zsh,
            "fish" => ShellKind.Fish,
            _ => throw new InvalidOperationException(
                "Unknown shell. Supported shells: powershell, bash, zsh, fish.")
        };

        // Raw stdout: the script is meant to be sourced or saved verbatim.
        global::System.Console.Out.Write(CompletionScripts.Render(shell));
        return Task.FromResult(0);
    }
}
