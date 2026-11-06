using DotVer.Domain;

namespace DotVer.Application;

/// <summary>
/// Derives the platform identity (OS token, architecture, RID) the same way
/// the .NET host and the official install scripts do, so the archives dotver
/// downloads are the ones <c>dotnet.exe</c> would run.
///
/// Observed in the shipped host: <c>hostfxr.dll</c> answers <c>DOTNET_RUNTIME_ID</c>
/// when set and otherwise a compiled-in RID (<c>win-x64</c> in the x64 Windows
/// build), and prints <c>arch=x64</c> next to it. <c>dotnet-install.sh</c> maps
/// <c>uname</c> to <c>osx</c>/<c>freebsd</c>/<c>linux</c>, detects musl through
/// <c>ldd --version</c>, and normalizes <c>uname -m</c> to x64/arm/arm64/s390x/
/// ppc64le/loongarch64/riscv64. See docs/reverse-engineering/dotnet-host-resolution.md.
/// </summary>
public static class HostPlatformResolver
{
    // Longest prefix first so "linux-musl-x64" is never read as linux + "musl-x64".
    private static readonly string[] OsTokens =
    [
        HostPlatform.LinuxMuslOs,
        HostPlatform.WindowsOs,
        HostPlatform.LinuxOs,
        HostPlatform.MacOs,
        HostPlatform.FreeBsdOs
    ];

    private static readonly Dictionary<string, string> OsAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["win"] = HostPlatform.WindowsOs,
        ["windows"] = HostPlatform.WindowsOs,
        ["linux"] = HostPlatform.LinuxOs,
        ["linux-musl"] = HostPlatform.LinuxMuslOs,
        ["musl"] = HostPlatform.LinuxMuslOs,
        ["alpine"] = HostPlatform.LinuxMuslOs,
        ["osx"] = HostPlatform.MacOs,
        ["macos"] = HostPlatform.MacOs,
        ["darwin"] = HostPlatform.MacOs,
        ["freebsd"] = HostPlatform.FreeBsdOs
    };

    // uname -m style spellings accepted next to the normalized names.
    private static readonly Dictionary<string, string> ArchitectureAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["amd64"] = "x64",
        ["x86_64"] = "x64",
        ["x64"] = "x64",
        ["i386"] = "x86",
        ["i686"] = "x86",
        ["x86"] = "x86",
        ["aarch64"] = "arm64",
        ["arm64"] = "arm64",
        ["armv7l"] = "arm",
        ["armv8l"] = "arm",
        ["armhf"] = "arm",
        ["arm"] = "arm",
        ["s390x"] = "s390x",
        ["ppc64le"] = "ppc64le",
        ["loongarch64"] = "loongarch64",
        ["riscv64"] = "riscv64"
    };

    /// <summary>Every RID dotver can download archives for (what the official metadata publishes).</summary>
    public static IReadOnlyList<string> SupportedRids { get; } =
    [
        "win-x64", "win-x86", "win-arm64",
        "linux-x64", "linux-arm", "linux-arm64", "linux-s390x", "linux-ppc64le", "linux-loongarch64", "linux-riscv64",
        "linux-musl-x64", "linux-musl-arm", "linux-musl-arm64", "linux-musl-s390x",
        "osx-x64", "osx-arm64",
        "freebsd-x64", "freebsd-arm64"
    ];

    /// <summary>The platform dotver itself is running on.</summary>
    /// <param name="environment">Supplies the OS flags and the process architecture.</param>
    /// <param name="fileExists">Probe used for musl detection; defaults to the file system.</param>
    public static HostPlatform Resolve(IEnvironmentContext environment, Func<string, bool>? fileExists = null)
    {
        ArgumentNullException.ThrowIfNull(environment);
        fileExists ??= File.Exists;

        var architecture = NormalizeArchitecture(environment.RuntimeArchitecture, out var muslArchitecture);
        string os;
        if (environment.IsWindows)
        {
            os = HostPlatform.WindowsOs;
        }
        else if (environment.IsMacOS)
        {
            os = HostPlatform.MacOs;
        }
        else if (environment.IsLinux)
        {
            os = muslArchitecture || IsMuslLibc(architecture, fileExists) ? HostPlatform.LinuxMuslOs : HostPlatform.LinuxOs;
        }
        else
        {
            os = OperatingSystem.IsFreeBSD() ? HostPlatform.FreeBsdOs : HostPlatform.LinuxOs;
        }

        return FromParts(os, architecture);
    }

    public static HostPlatform FromParts(string os, string architecture)
    {
        var normalizedOs = NormalizeOs(os);
        var normalizedArchitecture = NormalizeArchitecture(architecture, out var musl);
        if (musl && string.Equals(normalizedOs, HostPlatform.LinuxOs, StringComparison.Ordinal))
        {
            normalizedOs = HostPlatform.LinuxMuslOs;
        }

        if (string.Equals(normalizedOs, HostPlatform.WindowsOs, StringComparison.Ordinal) &&
            normalizedArchitecture is not ("x64" or "x86" or "arm64"))
        {
            throw new InvalidOperationException(
                $"Windows archives exist for x64, x86 and arm64 only, not '{TerminalText.Sanitize(architecture.Trim())}'.");
        }

        return new HostPlatform(
            normalizedOs,
            normalizedArchitecture,
            normalizedOs + "-" + normalizedArchitecture,
            ArchiveExtensionFor(normalizedOs));
    }

    /// <summary>Parses a RID such as <c>linux-musl-arm64</c>; the OS token is matched longest-first.</summary>
    public static bool TryParseRid(string? rid, out HostPlatform? platform)
    {
        platform = null;
        if (string.IsNullOrWhiteSpace(rid))
        {
            return false;
        }

        var value = rid.Trim().ToLowerInvariant();
        foreach (var os in OsTokens)
        {
            if (!value.StartsWith(os + "-", StringComparison.Ordinal))
            {
                continue;
            }

            var architecture = value[(os.Length + 1)..];
            if (!ArchitectureAliases.ContainsKey(architecture))
            {
                return false;
            }

            try
            {
                platform = FromParts(os, architecture);
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        return false;
    }

    public static HostPlatform FromRid(string rid) =>
        TryParseRid(rid, out var platform)
            ? platform!
            : throw new InvalidOperationException(
                $"Unknown runtime identifier '{TerminalText.Sanitize(rid?.Trim() ?? string.Empty)}'. Supported: {string.Join(", ", SupportedRids)}.");

    public static string NormalizeOs(string os)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(os);
        return OsAliases.TryGetValue(os.Trim(), out var normalized)
            ? normalized
            : throw new InvalidOperationException(
                $"Unknown operating system '{TerminalText.Sanitize(os.Trim())}'. Supported: windows, linux, linux-musl, osx, freebsd.");
    }

    /// <summary>Windows publishes .zip, every other OS .tar.gz (the formats dotver can extract offline).</summary>
    public static string ArchiveExtensionFor(string os) =>
        string.Equals(NormalizeOs(os), HostPlatform.WindowsOs, StringComparison.Ordinal) ? ".zip" : ".tar.gz";

    private static string NormalizeArchitecture(string architecture, out bool musl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(architecture);
        var value = architecture.Trim();
        musl = false;
        if (value.StartsWith("musl-", StringComparison.OrdinalIgnoreCase))
        {
            // dotver's --arch accepts the install-script spelling (musl-x64).
            musl = true;
            value = value[5..];
        }

        if (ArchitectureAliases.TryGetValue(value, out var normalized))
        {
            return normalized;
        }

        var supported = string.Join(", ", ArchitectureAliases.Values.Distinct(StringComparer.Ordinal).OrderBy(static a => a, StringComparer.Ordinal));
        throw new InvalidOperationException($"Unknown architecture '{TerminalText.Sanitize(value)}'. Supported: {supported}.");
    }

    // musl ships its dynamic loader as /lib/ld-musl-<machine>.so.1; glibc never does.
    private static bool IsMuslLibc(string architecture, Func<string, bool> fileExists)
    {
        var machine = architecture switch
        {
            "x64" => "x86_64",
            "x86" => "i386",
            "arm64" => "aarch64",
            "arm" => "armhf",
            _ => architecture
        };

        return fileExists("/lib/ld-musl-" + machine + ".so.1");
    }
}
