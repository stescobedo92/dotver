using Spectre.Console.Cli;

namespace DotVer.Cli;

internal sealed class UpgradeSettings : JsonSettings
{
    [CommandArgument(0, "[TOKEN]")]
    public string? Token { get; init; }

    [CommandOption("--all")]
    public bool All { get; init; }

    [CommandOption("--prune-old")]
    public bool PruneOld { get; init; }
}
