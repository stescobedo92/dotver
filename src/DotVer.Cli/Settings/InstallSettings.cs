using Spectre.Console.Cli;

namespace DotVer.Cli;

internal sealed class InstallSettings : CommandSettings
{
    [CommandArgument(0, "<VERSION_OR_CHANNEL>")]
    public string VersionOrChannel { get; init; } = string.Empty;

    [CommandOption("--no-use")]
    public bool NoUse { get; init; }

    [CommandOption("--no-default")]
    public bool NoDefault { get; init; }
}
