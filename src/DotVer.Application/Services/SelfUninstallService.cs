using DotVer.Domain;

namespace DotVer.Application;

public interface ISelfUninstallService
{
    /// <summary>What <see cref="UninstallAsync"/> would do. Never changes anything.</summary>
    Task<SelfUninstallPlan> PlanAsync(bool keepSdks, CancellationToken cancellationToken);

    Task<SelfUninstallOutcome> UninstallAsync(bool keepSdks, CancellationToken cancellationToken);
}

/// <summary>
/// Reverses everything dotver did to the machine: profile blocks, the user
/// PATH entry and DOTVER_HOME on Windows, and ~/.dotver itself (optionally
/// keeping the SDK payloads). The binary is left for the user or the package
/// manager that installed it, because a running image cannot delete itself.
/// </summary>
public sealed class SelfUninstallService : ISelfUninstallService
{
    private static readonly string[] KeptWithSdks = ["installs", "runtimes"];

    private readonly DotVerPaths _paths;
    private readonly IEnvironmentContext _environment;
    private readonly IEnvironmentBootstrapService _bootstrap;

    public SelfUninstallService(DotVerPaths paths, IEnvironmentContext environment, IEnvironmentBootstrapService bootstrap)
    {
        _paths = paths;
        _environment = environment;
        _bootstrap = bootstrap;
    }

    public Task<SelfUninstallPlan> PlanAsync(bool keepSdks, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var directories = new List<string>();
        var files = new List<string>();
        if (Directory.Exists(_paths.Home))
        {
            if (!keepSdks)
            {
                directories.Add(_paths.Home);
            }
            else
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(_paths.Home).OrderBy(static e => e, StringComparer.Ordinal))
                {
                    if (KeptWithSdks.Contains(Path.GetFileName(entry), StringComparer.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    (Directory.Exists(entry) ? directories : files).Add(entry);
                }
            }
        }

        var removePath = false;
        var removeVariables = false;
        if (_environment.IsWindows)
        {
            var userPath = _environment.GetUserEnvironmentVariable("PATH") ?? string.Empty;
            removePath = userPath
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Any(entry => IsShims(Environment.ExpandEnvironmentVariables(entry)));
            // Only the variable that points at this home: a custom DOTVER_HOME
            // must not uninstall another dotver installation's setting.
            removeVariables = string.Equals(_environment.GetUserEnvironmentVariable("DOTVER_HOME"), _paths.Home, StringComparison.OrdinalIgnoreCase);
        }

        var binary = _environment.ProcessPath;
        return Task.FromResult(new SelfUninstallPlan(
            _bootstrap.FindConfiguredProfiles(),
            directories,
            files,
            removePath,
            removeVariables,
            binary,
            BinaryRemovalHint(binary)));
    }

    public async Task<SelfUninstallOutcome> UninstallAsync(bool keepSdks, CancellationToken cancellationToken)
    {
        var plan = await PlanAsync(keepSdks, cancellationToken).ConfigureAwait(false);
        var removal = await _bootstrap.RemoveAsync(cancellationToken).ConfigureAwait(false);

        var deleted = new List<string>();
        var errors = new List<string>();
        foreach (var file in plan.FilesToDelete)
        {
            try
            {
                File.Delete(file);
                deleted.Add(file);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                errors.Add($"{TerminalText.Sanitize(file)}: {TerminalText.Sanitize(exception.Message)}");
            }
        }

        foreach (var directory in plan.DirectoriesToDelete)
        {
            try
            {
                DeleteDirectory(directory);
                deleted.Add(directory);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                errors.Add($"{TerminalText.Sanitize(directory)}: {TerminalText.Sanitize(exception.Message)}");
            }
        }

        return new SelfUninstallOutcome(plan, Executed: true, removal, deleted, errors);
    }

    // A link or junction at any of these paths is removed as a link, never
    // followed into whatever it points at.
    private static void DeleteDirectory(string directory)
    {
        var info = new DirectoryInfo(directory);
        if (!info.Exists)
        {
            return;
        }

        if (info.LinkTarget is not null || (info.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            info.Delete();
            return;
        }

        Directory.Delete(directory, recursive: true);
    }

    private bool IsShims(string entry)
    {
        var comparison = _environment.IsWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(
            entry.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            _paths.Shims.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            comparison);
    }

    private static string BinaryRemovalHint(string binary)
    {
        var name = Path.GetFileNameWithoutExtension(binary);
        if (!string.Equals(name, "dotver", StringComparison.OrdinalIgnoreCase))
        {
            return $"dotver runs through the dotnet host ({binary}); delete the directory that holds dotver.dll.";
        }

        if (binary.Contains("node_modules", StringComparison.OrdinalIgnoreCase))
        {
            return $"Installed through npm: run 'npm uninstall -g dotver' to remove {binary}.";
        }

        if (binary.Contains("WinGet", StringComparison.OrdinalIgnoreCase))
        {
            return $"Installed through WinGet: run 'winget uninstall DotVer.DotVer' to remove {binary}.";
        }

        return $"Delete the binary yourself: {binary} (a running executable cannot delete itself).";
    }
}
