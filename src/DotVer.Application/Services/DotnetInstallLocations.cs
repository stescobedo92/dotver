namespace DotVer.Application;

/// <summary>A dotnet root the host would consider global, and where that knowledge came from.</summary>
public sealed record GlobalInstallLocation(string Path, string Source);

/// <summary>
/// Where the .NET host looks for a machine-wide installation, per OS, in the
/// order it uses. Mirrors what <c>hostfxr.dll</c> 10.0.14 does on Windows
/// (registry value, then the default under Program Files) and what the host
/// does on Unix (<c>/etc/dotnet/install_location[_&lt;arch&gt;]</c>, then
/// <c>/usr/share/dotnet</c>, or <c>/usr/local/share/dotnet</c> on macOS).
/// The Windows half was recovered from the binary; see
/// docs/reverse-engineering/dotnet-host-resolution.md for the evidence.
/// </summary>
public static class DotnetInstallLocations
{
    /// <summary>Key under HKLM; the host opens it through the 32-bit registry view (KEY_WOW64_32KEY), i.e. WOW6432Node on 64-bit Windows.</summary>
    public const string WindowsRegistryRoot = @"SOFTWARE\dotnet\Setup\InstalledVersions";

    public const string WindowsRegistryValueName = "InstallLocation";

    public const string UnixInstallLocationDirectory = "/etc/dotnet";

    public static string WindowsRegistryKey(string architecture)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(architecture);
        return WindowsRegistryRoot + "\\" + architecture.Trim().ToLowerInvariant();
    }

    /// <summary>
    /// <c>%ProgramFiles%\dotnet</c>, or <c>%ProgramFiles(x86)%\dotnet</c> for x86;
    /// an x64 install on an arm64 host lives in <c>%ProgramFiles%\dotnet\x64</c>.
    /// </summary>
    public static string? WindowsDefaultInstallRoot(string? programFiles, string? programFilesX86, string architecture, string? hostArchitecture = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(architecture);
        var arch = architecture.Trim().ToLowerInvariant();
        if (arch == "x86")
        {
            return string.IsNullOrWhiteSpace(programFilesX86) ? null : Path.Combine(programFilesX86, "dotnet");
        }

        if (string.IsNullOrWhiteSpace(programFiles))
        {
            return null;
        }

        var root = Path.Combine(programFiles, "dotnet");
        return arch == "x64" && string.Equals(hostArchitecture?.Trim(), "arm64", StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(root, "x64")
            : root;
    }

    /// <summary>Architecture-specific file first, generic file second; the first one that exists wins.</summary>
    public static IReadOnlyList<string> UnixInstallLocationFiles(string architecture)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(architecture);
        return
        [
            UnixInstallLocationDirectory + "/install_location_" + architecture.Trim().ToLowerInvariant(),
            UnixInstallLocationDirectory + "/install_location"
        ];
    }

    public static string UnixDefaultInstallRoot(bool isMacOS) => isMacOS ? "/usr/local/share/dotnet" : "/usr/share/dotnet";

    /// <summary>The file holds one absolute path, possibly followed by a newline; blank files mean "not registered".</summary>
    public static string? ParseInstallLocationFile(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        var line = content
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(static candidate => candidate.Length > 0);
        if (string.IsNullOrWhiteSpace(line) || !line.StartsWith('/'))
        {
            return null;
        }

        var trimmed = line.TrimEnd('/');
        return trimmed.Length == 0 ? "/" : trimmed;
    }

    /// <summary>
    /// Registered location first, default location second, duplicates removed
    /// (compared case-insensitively on Windows, like the host does).
    /// </summary>
    /// <param name="environment">Supplies the OS, the architecture and the Program Files variables.</param>
    /// <param name="readRegistryInstallLocation">Reads the InstallLocation value of the given HKLM key path (32-bit view); null when absent or on non-Windows.</param>
    /// <param name="readFile">Reads a file's text; null when it does not exist.</param>
    public static IReadOnlyList<GlobalInstallLocation> Resolve(
        IEnvironmentContext environment,
        Func<string, string?> readRegistryInstallLocation,
        Func<string, string?> readFile)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(readRegistryInstallLocation);
        ArgumentNullException.ThrowIfNull(readFile);

        var architecture = environment.RuntimeArchitecture;
        var locations = new List<GlobalInstallLocation>();
        var comparison = environment.IsWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        void Add(string? path, string source)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            var normalized = path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (normalized.Length == 0)
            {
                normalized = path.Trim();
            }

            if (locations.Any(existing => string.Equals(existing.Path, normalized, comparison)))
            {
                return;
            }

            locations.Add(new GlobalInstallLocation(normalized, source));
        }

        if (environment.IsWindows)
        {
            var key = WindowsRegistryKey(architecture);
            Add(readRegistryInstallLocation(key), @"HKLM\" + key + "\\" + WindowsRegistryValueName);
            Add(
                WindowsDefaultInstallRoot(
                    environment.GetEnvironmentVariable("ProgramFiles"),
                    environment.GetEnvironmentVariable("ProgramFiles(x86)"),
                    architecture),
                "default (%ProgramFiles%\\dotnet)");
            return locations;
        }

        foreach (var file in UnixInstallLocationFiles(architecture))
        {
            var registered = ParseInstallLocationFile(readFile(file));
            if (registered is not null)
            {
                Add(registered, file);
                break;
            }
        }

        Add(UnixDefaultInstallRoot(environment.IsMacOS), "default");
        return locations;
    }
}
