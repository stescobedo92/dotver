namespace DotVer.Application;

/// <summary>
/// Finds executables on PATH without ever resolving through the current directory
/// (Windows process creation otherwise prefers cwd, which may be an untrusted repo).
/// </summary>
public static class ExecutableLocator
{
    public static string? FindOnPath(IEnvironmentContext environment, string fileName) =>
        FindOnPath(environment, fileName, excludedDirectories: null);

    /// <summary>
    /// Like <see cref="FindOnPath(IEnvironmentContext, string)"/>, additionally skipping
    /// <paramref name="excludedDirectories"/> even when they are on PATH (e.g. dotver's own
    /// shims directory when the shim is looking for the real host).
    /// </summary>
    public static string? FindOnPath(IEnvironmentContext environment, string fileName, IReadOnlyCollection<string>? excludedDirectories)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        if (fileName.Contains(Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            fileName.Contains(Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new ArgumentException("Only a bare executable file name is allowed.", nameof(fileName));
        }

        var comparison = environment.IsWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var skipped = new List<string> { Normalize(environment.CurrentDirectory) };
        foreach (var excluded in excludedDirectories ?? [])
        {
            if (!string.IsNullOrWhiteSpace(excluded) && Path.IsPathFullyQualified(excluded))
            {
                skipped.Add(Normalize(excluded));
            }
        }

        foreach (var entry in environment.GetPathEntries())
        {
            if (string.IsNullOrWhiteSpace(entry) || !Path.IsPathFullyQualified(entry))
            {
                continue;
            }

            var fullEntry = Normalize(entry);
            if (skipped.Any(directory => string.Equals(fullEntry, directory, comparison)))
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

    private static string Normalize(string directory) =>
        Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
