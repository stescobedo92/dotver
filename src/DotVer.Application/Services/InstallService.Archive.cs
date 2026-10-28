using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using DotVer.Domain;

namespace DotVer.Application;

/// <summary>
/// Offline installs: an official SDK archive (.zip or .tar.gz) already on
/// disk, verified against a digest the user obtained out of band, extracted
/// with the same containment rules as every other write under ~/.dotver.
/// </summary>
public sealed partial class InstallService
{
    private const UnixFileMode PermissionBits =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
        UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
        UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;

    public async Task<InstallOutcome> InstallFromArchiveAsync(
        string version,
        string archivePath,
        ArchiveChecksum checksum,
        InstallOptions? options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(checksum);
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);

        var token = version?.Trim() ?? string.Empty;
        if (!SdkVersion.TryParse(token, out var parsed))
        {
            throw new InvalidOperationException(
                $"--from needs the exact SDK version contained in the archive (e.g. 8.0.412), not '{TerminalText.Sanitize(token)}'.");
        }

        var architecture = Architectures.Normalize(options?.Architecture, _environment.RuntimeArchitecture);
        var fullArchive = Path.GetFullPath(archivePath, _environment.CurrentDirectory);
        if (!File.Exists(fullArchive))
        {
            throw new InvalidOperationException($"Archive '{TerminalText.Sanitize(fullArchive)}' does not exist.");
        }

        await VerifyChecksumAsync(fullArchive, checksum, cancellationToken).ConfigureAwait(false);

        var hostFileName = _environment.IsWindows ? "dotnet.exe" : "dotnet";
        var plan = new InstallPlan(
            token,
            parsed!.Raw,
            $"Installed from local archive {Path.GetFileName(fullArchive)} ({checksum.Algorithm.ToLowerInvariant()} verified).",
            null);

        Directory.CreateDirectory(_paths.ManagedInstalls);
        var installRoot = ResolveManagedInstallRoot(ManagedLeaf(parsed.Raw, architecture));
        if (Directory.Exists(installRoot))
        {
            var existing = await FindManagedAsync(parsed.Raw, architecture, cancellationToken).ConfigureAwait(false);
            return existing is not null
                ? new InstallOutcome(plan, existing, true)
                : throw new InvalidOperationException(
                    $"'{TerminalText.Sanitize(installRoot)}' exists but is not a complete SDK install; remove it first.");
        }

        // Extract next to the final location, validate the layout, then move:
        // the managed root never holds a half-extracted SDK.
        var staging = ResolveContainedRoot(_paths.ManagedInstalls, $".staging-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(staging);
            await ExtractArchiveAsync(fullArchive, staging, cancellationToken).ConfigureAwait(false);

            if (!File.Exists(Path.Combine(staging, hostFileName)) || !Directory.Exists(Path.Combine(staging, "sdk", parsed.Raw)))
            {
                throw new InvalidOperationException(
                    $"The archive is not a .NET SDK {parsed.Raw} layout (expected {hostFileName} and sdk/{parsed.Raw} at its root).");
            }

            if (!OperatingSystem.IsWindows())
            {
                var host = Path.Combine(staging, hostFileName);
                File.SetUnixFileMode(host, File.GetUnixFileMode(host) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
            }

            Directory.Move(staging, installRoot);
        }
        catch
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }

            throw;
        }

        var installed = await FindManagedAsync(parsed.Raw, architecture, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Could not validate the installation of {parsed.Raw} ({architecture}) from the archive.");
        return new InstallOutcome(plan, installed, false);
    }

    private async Task<SdkInstallation?> FindManagedAsync(string version, string architecture, CancellationToken cancellationToken)
    {
        var inventory = await _inventoryService.ScanAsync(forceRefresh: true, cancellationToken).ConfigureAwait(false);
        return inventory.FirstOrDefault(sdk => sdk.IsManaged &&
                                               string.Equals(sdk.Version.Raw, version, StringComparison.OrdinalIgnoreCase) &&
                                               Architectures.MatchesHost(sdk.Architecture ?? _environment.RuntimeArchitecture, architecture));
    }

    private static async Task VerifyChecksumAsync(string path, ArchiveChecksum checksum, CancellationToken cancellationToken)
    {
        var algorithm = checksum.Algorithm.Trim().ToLowerInvariant();
        var expected = checksum.Hex.Trim().ToLowerInvariant();
        var expectedLength = algorithm switch
        {
            "sha256" => 64,
            "sha512" => 128,
            _ => throw new InvalidOperationException("Supported checksum algorithms: sha256, sha512.")
        };

        if (expected.Length != expectedLength || !expected.All(static c => char.IsAsciiHexDigit(c)))
        {
            throw new InvalidOperationException($"The {algorithm} value must be {expectedLength} hexadecimal characters.");
        }

        await using var stream = File.OpenRead(path);
        var actual = algorithm == "sha256"
            ? await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false)
            : await SHA512.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        var actualHex = Convert.ToHexStringLower(actual);
        if (!string.Equals(actualHex, expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Checksum mismatch for {Path.GetFileName(path)}: expected {algorithm} {expected}, got {actualHex}. Nothing was extracted.");
        }
    }

    private async Task ExtractArchiveAsync(string archive, string staging, CancellationToken cancellationToken)
    {
        var name = Path.GetFileName(archive);
        if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            ExtractZip(archive, staging, cancellationToken);
            return;
        }

        if (name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase))
        {
            await ExtractTarGzAsync(archive, staging, cancellationToken).ConfigureAwait(false);
            return;
        }

        throw new InvalidOperationException("Supported archive formats: .zip, .tar.gz, .tgz.");
    }

    private void ExtractZip(string archive, string staging, CancellationToken cancellationToken)
    {
        using var zip = ZipFile.OpenRead(archive);
        foreach (var entry in zip.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var unixMode = (entry.ExternalAttributes >> 16) & 0xFFFF;
            if ((unixMode & 0xF000) == 0xA000)
            {
                throw new InvalidOperationException($"Refusing to extract '{TerminalText.Sanitize(entry.FullName)}': symbolic links are not allowed in an SDK archive.");
            }

            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
            {
                Directory.CreateDirectory(ResolveEntryPath(staging, entry.FullName));
                continue;
            }

            var destination = ResolveEntryPath(staging, entry.FullName);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using (var source = entry.Open())
            using (var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                source.CopyTo(target);
            }

            if (!OperatingSystem.IsWindows() && (unixMode & 0x1FF) != 0)
            {
                File.SetUnixFileMode(destination, (UnixFileMode)(unixMode & 0x1FF));
            }
        }
    }

    private async Task ExtractTarGzAsync(string archive, string staging, CancellationToken cancellationToken)
    {
        await using var file = File.OpenRead(archive);
        await using var gzip = new GZipStream(file, CompressionMode.Decompress);
        await using var reader = new TarReader(gzip, leaveOpen: false);

        while (await reader.GetNextEntryAsync(copyData: false, cancellationToken).ConfigureAwait(false) is { } entry)
        {
            switch (entry.EntryType)
            {
                case TarEntryType.Directory:
                    Directory.CreateDirectory(ResolveEntryPath(staging, entry.Name));
                    break;

                case TarEntryType.RegularFile:
                case TarEntryType.V7RegularFile:
                case TarEntryType.ContiguousFile:
                {
                    var destination = ResolveEntryPath(staging, entry.Name);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    await using (var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        if (entry.DataStream is not null)
                        {
                            await entry.DataStream.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
                        }
                    }

                    if (!OperatingSystem.IsWindows())
                    {
                        File.SetUnixFileMode(destination, entry.Mode & PermissionBits);
                    }

                    break;
                }

                case TarEntryType.ExtendedAttributes:
                case TarEntryType.GlobalExtendedAttributes:
                case TarEntryType.LongLink:
                case TarEntryType.LongPath:
                    break;

                default:
                    throw new InvalidOperationException(
                        $"Refusing to extract '{TerminalText.Sanitize(entry.Name)}': links and special files ({entry.EntryType}) are not allowed in an SDK archive.");
            }
        }
    }

    // Every entry must land strictly inside the staging directory: no rooted
    // names, no '..' segments, no drive-relative tricks (zip slip).
    private string ResolveEntryPath(string staging, string entryName)
    {
        var normalized = entryName.Replace('\\', '/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || Path.IsPathRooted(normalized) || normalized.StartsWith('/') ||
            segments.Any(static segment => segment == ".." || segment == "." || segment.Contains(':')))
        {
            throw new InvalidOperationException($"Refusing to extract '{TerminalText.Sanitize(entryName)}': it escapes the install directory.");
        }

        var root = Path.GetFullPath(staging).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var destination = Path.GetFullPath(Path.Combine(root, Path.Combine(segments)));
        var comparison = _environment.IsWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!destination.StartsWith(root, comparison))
        {
            throw new InvalidOperationException($"Refusing to extract '{TerminalText.Sanitize(entryName)}': it escapes the install directory.");
        }

        return destination;
    }
}
