using Spectre.Console.Cli;

namespace DotVer.Cli;

internal sealed class DoctorSettings : JsonSettings
{
    [CommandOption("--fix")]
    public bool Fix { get; init; }
}
