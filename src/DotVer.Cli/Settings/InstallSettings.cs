using Spectre.Console.Cli;

namespace DotVer.Cli;

internal sealed class InstallSettings : CommandSettings
{
    [CommandArgument(0, "<VERSION_OR_CHANNEL>")]
    public string VersionOrChannel { get; init; } = string.Empty;
}
