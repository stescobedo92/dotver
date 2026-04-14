using DotVer.Domain;

namespace DotVer.Application;

public sealed class EnvironmentBootstrapService : IEnvironmentBootstrapService
{
    private const string MarkerStart = "# >>> dotver >>>";
    private const string MarkerEnd = "# <<< dotver <<<";

    private readonly DotVerPaths _paths;
    private readonly IEnvironmentContext _environment;
    private readonly IShimService _shimService;

    public EnvironmentBootstrapService(DotVerPaths paths, IEnvironmentContext environment, IShimService shimService)
    {
        _paths = paths;
        _environment = environment;
        _shimService = shimService;
    }

    public async Task<EnvironmentBootstrapResult> EnsureConfiguredAsync(CancellationToken cancellationToken)
    {
        var shimResult = await _shimService.InstallAsync(cancellationToken).ConfigureAwait(false);
        var profilesUpdated = new List<string>();
        var notes = new List<string>();

        var processPathUpdated = EnsureProcessPath();
        var persistentPathUpdated = _environment.IsWindows
            ? EnsureWindowsIntegration(profilesUpdated, notes)
            : EnsureUnixProfiles(profilesUpdated);

        EnsureDotVerHome();

        if (persistentPathUpdated)
        {
            notes.Add("Persistent environment configuration was updated.");
        }

        notes.Add(_environment.IsWindows
            ? "On Windows, dotver updates PATH and PowerShell profiles; open a new terminal for the shim to take priority over the system dotnet."
            : "Open a new terminal if your current shell does not automatically reload the profile.");

        return new EnvironmentBootstrapResult(_paths.Shims, persistentPathUpdated, processPathUpdated, profilesUpdated, notes);
    }

    private bool EnsureProcessPath()
    {
        var currentPath = _environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var entries = currentPath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (entries.Contains(_paths.Shims, _environment.IsWindows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal))
        {
            return false;
        }

        var updated = string.IsNullOrWhiteSpace(currentPath)
            ? _paths.Shims
            : _paths.Shims + Path.PathSeparator + currentPath;

        Environment.SetEnvironmentVariable("PATH", updated, EnvironmentVariableTarget.Process);
        return true;
    }

    private bool EnsureWindowsIntegration(List<string> profilesUpdated, List<string> notes)
    {
        var pathChanged = EnsureWindowsUserPath(notes);
        var profilesChanged = EnsureWindowsPowerShellProfiles(profilesUpdated);
        return pathChanged || profilesChanged;
    }

    private bool EnsureWindowsUserPath(List<string> notes)
    {
        var userPath = _environment.GetUserEnvironmentVariable("PATH") ?? string.Empty;
        var entries = userPath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (entries.Contains(_paths.Shims, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        var updated = string.IsNullOrWhiteSpace(userPath)
            ? _paths.Shims
            : _paths.Shims + Path.PathSeparator + userPath;

        _environment.SetUserEnvironmentVariable("PATH", updated);
        notes.Add($"Added {_paths.Shims} to user PATH.");
        return true;
    }

    private bool EnsureWindowsPowerShellProfiles(List<string> profilesUpdated)
    {
        var profiles = new[]
        {
            Path.Combine(_environment.DocumentsDirectory, "PowerShell", "Microsoft.PowerShell_profile.ps1"),
            Path.Combine(_environment.DocumentsDirectory, "WindowsPowerShell", "Microsoft.PowerShell_profile.ps1")
        };

        var changed = false;
        var snippet = $". \"{Path.Combine(_paths.Env, "dotver.ps1")}\"";
        foreach (var profile in profiles)
        {
            if (EnsureProfileContains(profile, snippet))
            {
                profilesUpdated.Add(profile);
                changed = true;
            }
        }

        return changed;
    }

    private bool EnsureUnixProfiles(List<string> profilesUpdated)
    {
        var targets = new[]
        {
            Path.Combine(_environment.HomeDirectory, ".profile"),
            Path.Combine(_environment.HomeDirectory, ".bashrc"),
            Path.Combine(_environment.HomeDirectory, ".zshrc"),
            Path.Combine(_environment.HomeDirectory, ".config", "fish", "config.fish")
        };

        var changed = false;
        foreach (var profile in targets)
        {
            var sourceSnippet = profile.EndsWith("config.fish", StringComparison.OrdinalIgnoreCase)
                ? $"source \"{Path.Combine(_paths.Env, "dotver.fish")}\""
                : $"source \"{Path.Combine(_paths.Env, "dotver.sh")}\"";
            if (EnsureProfileContains(profile, sourceSnippet))
            {
                profilesUpdated.Add(profile);
                changed = true;
            }
        }

        return changed;
    }

    private static bool EnsureProfileContains(string profile, string sourceSnippet)
    {
        var block = $"{MarkerStart}{Environment.NewLine}{sourceSnippet}{Environment.NewLine}{MarkerEnd}{Environment.NewLine}";
        Directory.CreateDirectory(Path.GetDirectoryName(profile)!);
        string existing;
        try
        {
            existing = File.Exists(profile) ? File.ReadAllText(profile) : string.Empty;
        }
        catch (IOException)
        {
            return false;
        }

        var startIdx = existing.IndexOf(MarkerStart, StringComparison.Ordinal);
        if (startIdx >= 0)
        {
            var endIdx = existing.IndexOf(MarkerEnd, startIdx, StringComparison.Ordinal);
            if (endIdx >= 0)
            {
                var endOfBlock = endIdx + MarkerEnd.Length;
                if (endOfBlock < existing.Length && existing[endOfBlock] == '\r') endOfBlock++;
                if (endOfBlock < existing.Length && existing[endOfBlock] == '\n') endOfBlock++;

                var currentBlock = existing[startIdx..endOfBlock];
                if (string.Equals(currentBlock, block, StringComparison.Ordinal))
                {
                    return false;
                }

                var updated = existing[..startIdx] + block + existing[endOfBlock..];
                File.WriteAllText(profile, updated);
                return true;
            }
        }

        File.AppendAllText(profile, (existing.EndsWith(Environment.NewLine, StringComparison.Ordinal) || existing.Length == 0 ? string.Empty : Environment.NewLine) + block);
        return true;
    }

    private void EnsureDotVerHome()
    {
        Environment.SetEnvironmentVariable("DOTVER_HOME", _paths.Home, EnvironmentVariableTarget.Process);
        if (_environment.IsWindows)
        {
            _environment.SetUserEnvironmentVariable("DOTVER_HOME", _paths.Home);
        }
    }
}
