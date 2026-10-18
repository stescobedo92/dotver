using Spectre.Console.Cli;

namespace DotVer.Cli;

internal sealed class InfoSettings : JsonSettings
{
    [CommandArgument(0, "[TOKEN]")]
    public string? Token { get; init; }
}
