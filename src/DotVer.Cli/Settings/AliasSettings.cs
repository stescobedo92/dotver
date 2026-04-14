using Spectre.Console.Cli;

namespace DotVer.Cli;

internal sealed class AliasSettings : CommandSettings
{
    [CommandArgument(0, "[NAME]")]
    public string? Name { get; init; }

    [CommandArgument(1, "[VERSION]")]
    public string? Version { get; init; }
}
