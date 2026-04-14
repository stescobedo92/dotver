using Spectre.Console.Cli;

namespace DotVer.Cli;

internal sealed class RegisterRootSettings : CommandSettings
{
    [CommandArgument(0, "<PATH>")]
    public string Path { get; init; } = string.Empty;
}
