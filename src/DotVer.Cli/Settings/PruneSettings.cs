using Spectre.Console.Cli;

namespace DotVer.Cli;

internal sealed class PruneSettings : JsonSettings
{
    [CommandOption("--keep-latest-per-channel")]
    public bool KeepLatestPerChannel { get; init; }

    [CommandOption("--yes")]
    public bool Yes { get; init; }
}
