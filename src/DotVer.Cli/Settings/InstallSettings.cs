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
