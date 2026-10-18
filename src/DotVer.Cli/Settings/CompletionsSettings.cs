using Spectre.Console.Cli;

namespace DotVer.Cli;

internal sealed class CompletionsSettings : CommandSettings
{
    [CommandArgument(0, "<SHELL>")]
    public string Shell { get; init; } = string.Empty;
}
