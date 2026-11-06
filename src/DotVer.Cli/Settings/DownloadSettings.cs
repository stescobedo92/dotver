using System.ComponentModel;
using Spectre.Console.Cli;

namespace DotVer.Cli;

internal sealed class DownloadSettings : JsonSettings
{
    [CommandArgument(0, "<VERSION_OR_CHANNEL>")]
    [Description("Versions or channels to download: 8.0.412, 8.0, lts, sts, latest. Several can be given: dotver download 8.0 9.0 10.0")]
    public string[] VersionsOrChannels { get; init; } = [];

    [CommandOption("--rid <RID>")]
    [Description("Runtime identifier to download for (win-x64, win-arm64, linux-x64, linux-musl-arm64, osx-arm64, ...). Repeatable; defaults to this machine's RID.")]
    public string[] Rids { get; init; } = [];

    [CommandOption("--out <DIR>")]
    [Description("Directory that receives the archives and their .sha512 sidecars (default: ./dotnet-sdk-archives).")]
    public string? Out { get; init; }

    [CommandOption("--dry-run")]
    [Description("Resolve versions, archive URLs and digests without downloading anything.")]
    public bool DryRun { get; init; }
}
