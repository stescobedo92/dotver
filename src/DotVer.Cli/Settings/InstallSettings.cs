using System.ComponentModel;
using Spectre.Console.Cli;

namespace DotVer.Cli;

internal sealed class InstallSettings : CommandSettings
{
    [CommandArgument(0, "<VERSION_OR_CHANNEL>")]
    [Description("Versions or channels to install, in order: 8.0.412, 8.0, lts, sts, latest. Several can be given: dotver install 8.0 9.0 10.0")]
    public string[] VersionsOrChannels { get; init; } = [];

    [CommandOption("--no-use")]
    public bool NoUse { get; init; }

    [CommandOption("--no-default")]
    public bool NoDefault { get; init; }

    [CommandOption("--arch <ARCH>")]
    public string? Architecture { get; init; }

    [CommandOption("--runtime <RUNTIME>")]
    public string? Runtime { get; init; }

    [CommandOption("--no-tools")]
    public bool NoTools { get; init; }

    [CommandOption("--from <ARCHIVE>")]
    public string? From { get; init; }

    [CommandOption("--sha256 <HEX>")]
    public string? Sha256 { get; init; }

    [CommandOption("--sha512 <HEX>")]
    public string? Sha512 { get; init; }
}
