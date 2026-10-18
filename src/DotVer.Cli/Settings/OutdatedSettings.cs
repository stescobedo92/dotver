using Spectre.Console.Cli;

namespace DotVer.Cli;

internal sealed class OutdatedSettings : JsonSettings
{
    [CommandOption("--managed-only")]
    public bool ManagedOnly { get; init; }

    [CommandOption("--all")]
    public bool All { get; init; }
}
