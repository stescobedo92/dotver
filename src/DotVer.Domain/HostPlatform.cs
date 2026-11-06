namespace DotVer.Domain;

/// <summary>
/// The platform identity the .NET host uses for itself: an OS family, an
/// architecture and the runtime identifier (RID) derived from both
/// (<c>win-x64</c>, <c>linux-musl-arm64</c>, <c>osx-arm64</c>). The official
/// release metadata keys every downloadable archive by that RID, and the host
/// prints it in <c>dotnet --info</c> and in the <c>aka.ms/dotnet-core-applaunch</c>
/// link it emits when a runtime is missing.
/// </summary>
/// <param name="Os">Normalized OS token as it appears in a RID: <c>win</c>, <c>linux</c>, <c>linux-musl</c>, <c>osx</c> or <c>freebsd</c>.</param>
/// <param name="Architecture">Normalized architecture: <c>x64</c>, <c>x86</c>, <c>arm64</c>, <c>arm</c>, <c>s390x</c>, <c>ppc64le</c>, <c>loongarch64</c>, <c>riscv64</c>.</param>
/// <param name="Rid">The runtime identifier: <c>{Os}-{Architecture}</c>.</param>
/// <param name="ArchiveExtension">Archive format Microsoft publishes for this OS: <c>.zip</c> on Windows, <c>.tar.gz</c> everywhere else.</param>
public sealed record HostPlatform(string Os, string Architecture, string Rid, string ArchiveExtension)
{
    public const string WindowsOs = "win";
    public const string LinuxOs = "linux";
    public const string LinuxMuslOs = "linux-musl";
    public const string MacOs = "osx";
    public const string FreeBsdOs = "freebsd";

    public bool IsWindows => string.Equals(Os, WindowsOs, StringComparison.Ordinal);

    public override string ToString() => Rid;
}

/// <summary>
/// The fixed links the .NET host hands out, and the exact query string it
/// builds for the "You must install or update .NET" message
/// (<c>missing_runtime=true</c> or <c>framework=...&amp;framework_version=...</c>,
/// then <c>&amp;arch=</c>, <c>&amp;rid=</c>, <c>&amp;os=</c>, optionally
/// <c>&amp;apphost_version=</c>). Recovered from <c>dotnet.exe</c> and
/// <c>hostfxr.dll</c>; see docs/reverse-engineering/dotnet-host-resolution.md.
/// </summary>
public static class DotnetHostLinks
{
    public const string AppLaunchBase = "https://aka.ms/dotnet-core-applaunch?";
    public const string AppLaunchFailed = "https://aka.ms/dotnet/app-launch-failed";
    public const string SdkDownload = "https://aka.ms/dotnet/download";
    public const string SdkNotFound = "https://aka.ms/dotnet/sdk-not-found";
    public const string Info = "https://aka.ms/dotnet/info";

    public static string MissingRuntimeUrl(
        string? framework,
        string? frameworkVersion,
        string architecture,
        string rid,
        string os,
        string? apphostVersion = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(architecture);
        ArgumentException.ThrowIfNullOrWhiteSpace(rid);
        ArgumentException.ThrowIfNullOrWhiteSpace(os);

        var url = AppLaunchBase;
        if (string.IsNullOrEmpty(framework))
        {
            url += "missing_runtime=true";
        }
        else
        {
            url += "framework=" + framework;
            if (!string.IsNullOrEmpty(frameworkVersion))
            {
                url += "&framework_version=" + frameworkVersion;
            }
        }

        url += "&arch=" + architecture + "&rid=" + rid + "&os=" + os;
        if (!string.IsNullOrEmpty(apphostVersion))
        {
            url += "&apphost_version=" + apphostVersion;
        }

        return url;
    }
}
