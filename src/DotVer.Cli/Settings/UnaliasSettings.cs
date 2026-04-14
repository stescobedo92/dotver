using Spectre.Console.Cli;

namespace DotVer.Cli;

internal sealed class UnaliasSettings : CommandSettings
{
    [CommandArgument(0, "<NAME>")]
    public string Name { get; init; } = string.Empty;
}
