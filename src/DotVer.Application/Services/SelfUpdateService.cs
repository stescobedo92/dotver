using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using DotVer.Domain;

namespace DotVer.Application;

public interface IReleaseFeed
{
    Task<ReleaseInfo> GetLatestAsync(string repository, CancellationToken cancellationToken);

    Task<ReleaseInfo> GetByTagAsync(string repository, string tag, CancellationToken cancellationToken);

    Task<byte[]> DownloadAssetAsync(string url, long maxBytes, CancellationToken cancellationToken);
}

public interface ISelfUpdateService
{
    Task<SelfUpdateCheck> CheckAsync(string? version, CancellationToken cancellationToken);

    Task<SelfUpdateOutcome> UpdateAsync(string? version, CancellationToken cancellationToken);
}

/// <summary>
/// Replaces the running standalone binary with a published release asset after
/// verifying it against the release's checksums.txt. Package-manager installs
/// (npm, WinGet) keep working but are better updated through their manager.
/// </summary>
public sealed partial class SelfUpdateService : ISelfUpdateService
{
    public const string DefaultRepository = "stescobedo92/dotver";
    public const string RepositoryVariable = "DOTVER_UPDATE_REPOSITORY";

    private const long MaxAssetBytes = 256L * 1024 * 1024;
    private const long MaxChecksumsBytes = 1024 * 1024;

    private readonly IReleaseFeed _feed;
    private readonly IEnvironmentContext _environment;

    public SelfUpdateService(IReleaseFeed feed, IEnvironmentContext environment)
    {
        _feed = feed;
        _environment = environment;
    }

    public async Task<SelfUpdateCheck> CheckAsync(string? version, CancellationToken cancellationToken)
    {
        var release = await ResolveReleaseAsync(version, cancellationToken).ConfigureAwait(false);
        return BuildCheck(release, version is not null);
    }

    public async Task<SelfUpdateOutcome> UpdateAsync(string? version, CancellationToken cancellationToken)
    {
        var release = await ResolveReleaseAsync(version, cancellationToken).ConfigureAwait(false);
        var check = BuildCheck(release, version is not null);
        if (!check.CanSelfUpdate)
        {
            throw new InvalidOperationException(check.Reason);
        }

        var assetName = check.AssetName!;
        if (!check.UpdateAvailable)
        {
            return new SelfUpdateOutcome(check.CurrentVersion, release.Version, check.ExecutablePath, assetName, null, Updated: false, "Already up to date.");
        }

        if (!release.AssetUrls.TryGetValue("checksums.txt", out var checksumsUrl))
        {
            throw new InvalidOperationException($"Release {release.Tag} publishes no checksums.txt; refusing to install an unverifiable binary.");
        }

        if (!release.AssetUrls.TryGetValue(assetName, out var assetUrl))
        {
            throw new InvalidOperationException($"Release {release.Tag} has no asset named {assetName} for this platform.");
        }

        var checksums = Encoding.UTF8.GetString(await _feed.DownloadAssetAsync(checksumsUrl, MaxChecksumsBytes, cancellationToken).ConfigureAwait(false));
        var expected = ParseChecksum(checksums, assetName)
            ?? throw new InvalidOperationException($"checksums.txt of release {release.Tag} has no SHA-256 for {assetName}; refusing to install.");

        var bytes = await _feed.DownloadAssetAsync(assetUrl, MaxAssetBytes, cancellationToken).ConfigureAwait(false);
        var actual = Convert.ToHexStringLower(SHA256.HashData(bytes));
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Checksum mismatch for {assetName}: expected {expected}, got {actual}. The binary was not replaced.");
        }

        ReplaceExecutable(check.ExecutablePath, bytes);
        var note = _environment.IsWindows
            ? $"The previous binary was kept as {check.ExecutablePath}.old (a running image cannot be deleted); the next self-update removes it."
            : null;
        return new SelfUpdateOutcome(check.CurrentVersion, release.Version, check.ExecutablePath, assetName, actual, Updated: true, note);
    }

    private async Task<ReleaseInfo> ResolveReleaseAsync(string? version, CancellationToken cancellationToken)
    {
        var repository = ResolveRepository();
        if (string.IsNullOrWhiteSpace(version))
        {
            return await _feed.GetLatestAsync(repository, cancellationToken).ConfigureAwait(false);
        }

        var trimmed = version.Trim();
        if (!VersionPattern().IsMatch(trimmed))
        {
            throw new InvalidOperationException($"'{TerminalText.Sanitize(trimmed)}' is not a release version (expected e.g. 0.4.0 or v0.4.0).");
        }

        var tag = trimmed.StartsWith('v') ? trimmed : "v" + trimmed;
        return await _feed.GetByTagAsync(repository, tag, cancellationToken).ConfigureAwait(false);
    }

    private SelfUpdateCheck BuildCheck(ReleaseInfo release, bool explicitVersion)
    {
        var current = _environment.ProductVersion;
        var assetName = AssetNameForHost();
        var executable = _environment.ProcessPath;
        var (canSelfUpdate, reason) = Capability(executable, assetName);
        var updateAvailable = explicitVersion
            ? CompareVersions(release.Version, current) != 0
            : CompareVersions(release.Version, current) > 0;

        return new SelfUpdateCheck(current, release.Version, release.Tag, updateAvailable, assetName, executable, canSelfUpdate, reason);
    }

    private (bool CanSelfUpdate, string? Reason) Capability(string executable, string? assetName)
    {
        var processName = Path.GetFileNameWithoutExtension(executable);
        if (!string.Equals(processName, "dotver", StringComparison.OrdinalIgnoreCase))
        {
            return (false, $"self-update only works for the standalone dotver binary; this process is '{TerminalText.Sanitize(processName)}' (for example 'dotnet dotver.dll'). Update dotver the way it was installed.");
        }

        if (assetName is null)
        {
            return (false, $"No release asset is published for this platform ({_environment.RuntimeArchitecture}).");
        }

        if (!File.Exists(executable))
        {
            return (false, $"The running executable was not found at '{TerminalText.Sanitize(executable)}'.");
        }

        return (true, null);
    }

    private string? AssetNameForHost()
    {
        var architecture = _environment.RuntimeArchitecture.ToLowerInvariant();
        if (_environment.IsWindows)
        {
            return architecture == "x64" ? "dotver-win-x64.exe" : null;
        }

        if (_environment.IsLinux)
        {
            return architecture == "x64" ? "dotver-linux-x64" : null;
        }

        if (_environment.IsMacOS)
        {
            return architecture switch
            {
                "arm64" => "dotver-osx-arm64",
                "x64" => "dotver-osx-x64",
                _ => null
            };
        }

        return null;
    }

    private string ResolveRepository()
    {
        var configured = _environment.GetEnvironmentVariable(RepositoryVariable)?.Trim();
        if (string.IsNullOrEmpty(configured))
        {
            return DefaultRepository;
        }

        return RepositoryPattern().IsMatch(configured)
            ? configured
            : throw new InvalidOperationException($"{RepositoryVariable} must look like owner/repo.");
    }

    private void ReplaceExecutable(string target, byte[] bytes)
    {
        var directory = Path.GetDirectoryName(target)!;
        var temp = Path.Combine(directory, $".dotver-update-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
            }

            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(temp,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }

            if (_environment.IsWindows)
            {
                // A running image can be renamed but not overwritten or deleted.
                var old = target + ".old";
                TryDelete(old);
                if (File.Exists(old))
                {
                    old = $"{target}.{Guid.NewGuid():N}.old";
                }

                File.Move(target, old);
                File.Move(temp, target);
                return;
            }

            File.Move(temp, target, overwrite: true);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Still in use by a previous instance; leave it.
        }
    }

    internal static string? ParseChecksum(string checksums, string assetName)
    {
        foreach (var rawLine in checksums.Split('\n'))
        {
            var parts = rawLine.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length < 2)
            {
                continue;
            }

            // Both "name  hash" (ours) and "hash  name" (sha256sum) layouts.
            var (name, hash) = parts[0].Length == 64 && parts[0].All(char.IsAsciiHexDigit)
                ? (parts[1].TrimStart('*'), parts[0])
                : (parts[0], parts[1]);
            if (string.Equals(name, assetName, StringComparison.OrdinalIgnoreCase) &&
                hash.Length == 64 && hash.All(char.IsAsciiHexDigit))
            {
                return hash.ToLowerInvariant();
            }
        }

        return null;
    }

    /// <summary>Compares release versions by numeric core; a prerelease sorts below the same core without suffix.</summary>
    internal static int CompareVersions(string left, string right)
    {
        var (leftCore, leftPre) = Split(left);
        var (rightCore, rightPre) = Split(right);
        var byCore = leftCore.CompareTo(rightCore);
        if (byCore != 0)
        {
            return byCore;
        }

        return (leftPre, rightPre) switch
        {
            (false, true) => 1,
            (true, false) => -1,
            _ => 0
        };

        static (Version Core, bool Prerelease) Split(string value)
        {
            var trimmed = value.Trim().TrimStart('v', 'V');
            var cut = trimmed.IndexOfAny(['-', '+']);
            var prerelease = cut >= 0 && trimmed[cut] == '-';
            var core = cut >= 0 ? trimmed[..cut] : trimmed;
            if (core.Count(static c => c == '.') == 1)
            {
                core += ".0";
            }

            return (Version.TryParse(core, out var parsed) ? parsed : new Version(0, 0, 0), prerelease);
        }
    }

    [GeneratedRegex(@"\A[vV]?[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?\z", RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();

    [GeneratedRegex(@"\A[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+\z", RegexOptions.CultureInvariant)]
    private static partial Regex RepositoryPattern();
}
