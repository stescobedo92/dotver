using Spectre.Console.Cli;

namespace DotVer.Cli;

internal sealed class ScanSettings : JsonSettings
{
    [CommandArgument(0, "[PATH]")]
    public string? Path { get; init; }
}
