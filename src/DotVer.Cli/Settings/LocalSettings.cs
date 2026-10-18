using Spectre.Console.Cli;

namespace DotVer.Cli;

internal sealed class LocalSettings : JsonSettings
{
    [CommandArgument(0, "[VERSION]")]
    public string? Version { get; init; }

    [CommandOption("--roll-forward <POLICY>")]
    public string? RollForward { get; init; }

    [CommandOption("--clear")]
    public bool Clear { get; init; }

    [CommandOption("--parent")]
    public bool Parent { get; init; }
}
