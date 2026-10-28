using System.Text.Json;
using System.Text.Json.Nodes;
using DotVer.Domain;

namespace DotVer.Application;

public sealed class EnvironmentBootstrapService : IEnvironmentBootstrapService
{
    /// <summary>Set to 1/true to keep dotver from touching PATH, shell profiles and the registry.</summary>
    public const string OptOutVariable = "DOTVER_NO_BOOTSTRAP";

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

    private string RecordPath => Path.Combine(_paths.Home, "bootstrap.json");

    public async Task<EnvironmentBootstrapResult> EnsureConfiguredAsync(CancellationToken cancellationToken)
    {
        // Shims live inside DOTVER_HOME, the one directory dotver owns. Everything
        // below mutates user-owned state and is subject to the opt-out.
        await _shimService.InstallAsync(cancellationToken).ConfigureAwait(false);

        if (IsOptedOut())
        {
            return new EnvironmentBootstrapResult(
                _paths.Shims,
                PersistentPathUpdated: false,
                ProcessPathUpdated: false,
                [],
                [
                    $"Bootstrap skipped: {OptOutVariable} is set, so PATH, shell profiles and the registry were left untouched. " +
                    $"Add {_paths.Shims} to PATH yourself (see the snippets in {_paths.Env}) or unset the variable."
                ]);
        }

        var profilesUpdated = new List<string>();
        var notes = new List<string>();
        var record = BootstrapRecord.Load(RecordPath);

        var processPathUpdated = EnsureProcessPath();
        var persistentPathUpdated = _environment.IsWindows
            ? EnsureWindowsIntegration(profilesUpdated, notes, record)
            : EnsureUnixProfiles(profilesUpdated, record);

        EnsureDotVerHome();
        record.SaveIfChanged(RecordPath);

        if (persistentPathUpdated)
        {
            notes.Add("Persistent environment configuration was updated.");
        }

        notes.Add(_environment.IsWindows
            ? "On Windows, dotver updates PATH and PowerShell profiles; open a new terminal for the shim to take priority over the system dotnet."
            : "Open a new terminal if your current shell does not automatically reload the profile.");

        return new EnvironmentBootstrapResult(_paths.Shims, persistentPathUpdated, processPathUpdated, profilesUpdated, notes);
    }

    private bool IsOptedOut()
    {
        var value = _environment.GetEnvironmentVariable(OptOutVariable)?.Trim();
        return value is not null &&
               (value == "1" ||
                string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase));
    }

    private bool EnsureProcessPath()
    {
        var currentPath = _environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var entries = currentPath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (entries.Any(IsShimsDirectory))
        {
            return false;
        }

        var updated = string.IsNullOrWhiteSpace(currentPath)
            ? _paths.Shims
            : _paths.Shims + Path.PathSeparator + currentPath;

        Environment.SetEnvironmentVariable("PATH", updated, EnvironmentVariableTarget.Process);
        return true;
    }

    private bool EnsureWindowsIntegration(List<string> profilesUpdated, List<string> notes, BootstrapRecord record)
    {
        var pathChanged = EnsureWindowsUserPath(notes);
        var profilesChanged = EnsureWindowsPowerShellProfiles(profilesUpdated, record);
        return pathChanged || profilesChanged;
    }

    private bool EnsureWindowsUserPath(List<string> notes)
    {
        // The user PATH is read unexpanded (REG_EXPAND_SZ text such as
        // %USERPROFILE%\.dotver\shims), so compare expanded entries and leave
        // the stored text untouched when nothing is missing.
        var userPath = _environment.GetUserEnvironmentVariable("PATH") ?? string.Empty;
        var entries = userPath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (entries.Any(entry => IsShimsDirectory(Environment.ExpandEnvironmentVariables(entry))))
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

    private bool IsShimsDirectory(string entry)
    {
        if (string.IsNullOrWhiteSpace(entry))
        {
            return false;
        }

        var comparison = _environment.IsWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(
            entry.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            _paths.Shims.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            comparison);
    }

    private bool EnsureWindowsPowerShellProfiles(List<string> profilesUpdated, BootstrapRecord record)
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
            if (EnsureProfileContains(profile, snippet, record))
            {
                profilesUpdated.Add(profile);
                changed = true;
            }
        }

        return changed;
    }

    private bool EnsureUnixProfiles(List<string> profilesUpdated, BootstrapRecord record)
    {
        var targets = new List<string>
        {
            Path.Combine(_environment.HomeDirectory, ".profile"),
            Path.Combine(_environment.HomeDirectory, ".bashrc"),
            Path.Combine(_environment.HomeDirectory, ".zshrc")
        };

        // fish creates its config directory on first run: configure it only
        // for users who actually have fish instead of planting ~/.config/fish.
        var fishDirectory = Path.Combine(_environment.HomeDirectory, ".config", "fish");
        if (Directory.Exists(fishDirectory))
        {
            targets.Add(Path.Combine(fishDirectory, "config.fish"));
        }

        var changed = false;
        foreach (var profile in targets)
        {
            var sourceSnippet = profile.EndsWith("config.fish", StringComparison.OrdinalIgnoreCase)
                ? $"source \"{Path.Combine(_paths.Env, "dotver.fish")}\""
                : $"source \"{Path.Combine(_paths.Env, "dotver.sh")}\"";
            if (EnsureProfileContains(profile, sourceSnippet, record))
            {
                profilesUpdated.Add(profile);
                changed = true;
            }
        }

        return changed;
    }

    private static bool EnsureProfileContains(string profile, string sourceSnippet, BootstrapRecord record)
    {
        var block = $"{MarkerStart}{Environment.NewLine}{sourceSnippet}{Environment.NewLine}{MarkerEnd}{Environment.NewLine}";

        // Dotfile setups symlink profiles into a repository: edit the real file
        // so the link survives. The home directory is trusted, so following is fine.
        var target = ResolveWriteTarget(profile);
        string existing;
        try
        {
            existing = File.Exists(target) ? File.ReadAllText(target) : string.Empty;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        var startIdx = existing.IndexOf(MarkerStart, StringComparison.Ordinal);
        if (startIdx >= 0)
        {
            var endIdx = existing.IndexOf(MarkerEnd, startIdx, StringComparison.Ordinal);
            if (endIdx >= 0)
            {
                record.Remember(profile);
                var endOfBlock = endIdx + MarkerEnd.Length;
                if (endOfBlock < existing.Length && existing[endOfBlock] == '\r') endOfBlock++;
                if (endOfBlock < existing.Length && existing[endOfBlock] == '\n') endOfBlock++;

                var currentBlock = existing[startIdx..endOfBlock];
                if (string.Equals(currentBlock, block, StringComparison.Ordinal))
                {
                    return false;
                }

                WriteAtomically(target, existing[..startIdx] + block + existing[endOfBlock..]);
                return true;
            }
        }

        // The block was added once and is gone now: the user removed it on
        // purpose, so never re-add it behind their back.
        if (record.Contains(profile))
        {
            return false;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var separator = existing.Length == 0 || existing.EndsWith(Environment.NewLine, StringComparison.Ordinal)
            ? string.Empty
            : Environment.NewLine;
        WriteAtomically(target, existing + separator + block);
        record.Remember(profile);
        return true;
    }

    private static string ResolveWriteTarget(string profile)
    {
        var info = new FileInfo(profile);
        if (info.LinkTarget is null)
        {
            return profile;
        }

        try
        {
            return info.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? profile;
        }
        catch (IOException)
        {
            return profile;
        }
    }

    private static void WriteAtomically(string target, string content)
    {
        var directory = Path.GetDirectoryName(target)!;
        var temp = Path.Combine(directory, $".{Path.GetFileName(target)}.{Guid.NewGuid():N}.tmp");
        UnixFileMode? mode = null;
        if (!OperatingSystem.IsWindows() && File.Exists(target))
        {
            mode = File.GetUnixFileMode(target);
        }

        try
        {
            // CreateNew: never write through a pre-planted file at the temp path.
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(content);
            }

            if (mode is not null && !OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(temp, mode.Value);
            }

            File.Move(temp, target, overwrite: true);
        }
        catch
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }

            throw;
        }
    }

    private void EnsureDotVerHome()
    {
        Environment.SetEnvironmentVariable("DOTVER_HOME", _paths.Home, EnvironmentVariableTarget.Process);
        if (_environment.IsWindows &&
            !string.Equals(_environment.GetUserEnvironmentVariable("DOTVER_HOME"), _paths.Home, StringComparison.OrdinalIgnoreCase))
        {
            _environment.SetUserEnvironmentVariable("DOTVER_HOME", _paths.Home);
        }
    }

    /// <summary>
    /// Remembers which profiles dotver has configured so a block the user
    /// deleted is not silently re-added on the next command.
    /// </summary>
    private sealed class BootstrapRecord
    {
        private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

        private readonly HashSet<string> _configuredProfiles = new(StringComparer.OrdinalIgnoreCase);
        private bool _dirty;

        public static BootstrapRecord Load(string path)
        {
            var record = new BootstrapRecord();
            if (!File.Exists(path))
            {
                return record;
            }

            try
            {
                var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
                if (root?["configuredProfiles"] is JsonArray profiles)
                {
                    foreach (var entry in profiles)
                    {
                        if (entry is JsonValue value && value.TryGetValue<string>(out var profile) && !string.IsNullOrWhiteSpace(profile))
                        {
                            record._configuredProfiles.Add(profile);
                        }
                    }
                }
            }
            catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
            {
                // A corrupt record only loses the "do not re-add" memory.
            }

            return record;
        }

        public bool Contains(string profile) => _configuredProfiles.Contains(profile);

        public void Remember(string profile)
        {
            if (_configuredProfiles.Add(profile))
            {
                _dirty = true;
            }
        }

        public void SaveIfChanged(string path)
        {
            if (!_dirty)
            {
                return;
            }

            var profiles = new JsonArray();
            foreach (var profile in _configuredProfiles.OrderBy(static p => p, StringComparer.Ordinal))
            {
                profiles.Add(profile);
            }

            var root = new JsonObject { ["configuredProfiles"] = profiles };
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            WriteAtomically(path, root.ToJsonString(WriteOptions) + Environment.NewLine);
            _dirty = false;
        }
    }
}
