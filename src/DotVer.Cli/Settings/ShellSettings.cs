using Spectre.Console.Cli;

namespace DotVer.Cli;

internal sealed class ShellSettings : CommandSettings
{
    [CommandArgument(0, "<TOKEN>")]
    public string Token { get; init; } = string.Empty;

    [CommandOption("--shell <SHELL>")]
    public string? Shell { get; init; }
}
