using Spectre.Console.Cli;

namespace DotVer.Cli;

internal abstract class JsonSettings : CommandSettings
{
    [CommandOption("--json")]
    public bool Json { get; init; }
}
