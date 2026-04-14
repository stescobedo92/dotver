using Spectre.Console.Cli;

namespace DotVer.Cli;

internal sealed class ListSettings : CommandSettings
{
    [CommandOption("--managed-only")]
    public bool ManagedOnly { get; init; }
}
