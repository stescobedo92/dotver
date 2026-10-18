using Spectre.Console.Cli;

namespace DotVer.Cli;

internal sealed class EnvSettings : JsonSettings
{
    [CommandArgument(0, "[TOKEN]")]
    public string? Token { get; init; }

    [CommandOption("--shell <SHELL>")]
    public string? Shell { get; init; }

    [CommandOption("--github")]
    public bool GitHub { get; init; }
}
