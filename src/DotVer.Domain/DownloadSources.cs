namespace DotVer.Domain;

/// <summary>Where install scripts and release metadata are fetched from, after applying the configured mirror.</summary>
public sealed record DownloadSources(
    string PowerShellInstallScriptUrl,
    string ShellInstallScriptUrl,
    string ReleasesIndexUrl,
    IReadOnlyList<string> ExtraAllowedHosts,
    bool IsMirrored)
{
    public const string OfficialPowerShellInstallScriptUrl = "https://dot.net/v1/dotnet-install.ps1";
    public const string OfficialShellInstallScriptUrl = "https://dot.net/v1/dotnet-install.sh";
    public const string OfficialReleasesIndexUrl = "https://builds.dotnet.microsoft.com/dotnet/release-metadata/releases-index.json";

    public static DownloadSources Official => new(
        OfficialPowerShellInstallScriptUrl,
        OfficialShellInstallScriptUrl,
        OfficialReleasesIndexUrl,
        [],
        IsMirrored: false);
}

/// <summary>Expected digest of a local SDK archive (<c>dotver install --from</c>).</summary>
public sealed record ArchiveChecksum(string Algorithm, string Hex);
