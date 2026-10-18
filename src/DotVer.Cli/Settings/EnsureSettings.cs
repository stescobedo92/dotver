using Spectre.Console.Cli;

namespace DotVer.Cli;

internal sealed class EnsureSettings : JsonSettings
{
    [CommandOption("--use")]
    public bool Use { get; init; }
}
