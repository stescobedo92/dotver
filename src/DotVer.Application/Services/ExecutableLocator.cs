namespace DotVer.Application;

/// <summary>
/// Finds executables on PATH without ever resolving through the current directory
/// (Windows process creation otherwise prefers cwd, which may be an untrusted repo).
/// </summary>
public static class ExecutableLocator
{
    public static string? FindOnPath(IEnvironmentContext environment, string fileName)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        if (fileName.Contains(Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            fileName.Contains(Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new ArgumentException("Only a bare executable file name is allowed.", nameof(fileName));
        }

        var comparison = environment.IsWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var currentDirectory = Path.GetFullPath(environment.CurrentDirectory);

        foreach (var entry in environment.GetPathEntries())
        {
            if (string.IsNullOrWhiteSpace(entry) || !Path.IsPathFullyQualified(entry))
            {
                continue;
            }

            var fullEntry = Path.GetFullPath(entry);
            if (string.Equals(fullEntry.TrimEnd(Path.DirectorySeparatorChar), currentDirectory.TrimEnd(Path.DirectorySeparatorChar), comparison))
            {
                continue;
            }

            var candidate = Path.Combine(fullEntry, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
