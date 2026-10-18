using Spectre.Console.Cli;

namespace DotVer.Cli;

internal sealed class EnsureSettings : JsonSettings
{
    [CommandArgument(0, "[PATH]")]
    public string? Path { get; init; }

    [CommandOption("--use")]
    public bool Use { get; init; }

    [CommandOption("--recursive")]
    public bool Recursive { get; init; }
}
