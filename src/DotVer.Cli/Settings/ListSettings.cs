using Spectre.Console.Cli;

namespace DotVer.Cli;

internal sealed class ListSettings : JsonSettings
{
    [CommandOption("--managed-only")]
    public bool ManagedOnly { get; init; }
}
