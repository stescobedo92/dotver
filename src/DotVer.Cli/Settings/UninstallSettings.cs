using Spectre.Console.Cli;

namespace DotVer.Cli;

internal sealed class UninstallSettings : CommandSettings
{
    [CommandArgument(0, "<VERSION>")]
    public string Version { get; init; } = string.Empty;
}
