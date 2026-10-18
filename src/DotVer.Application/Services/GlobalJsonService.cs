using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotVer.Domain;
using Microsoft.Win32.SafeHandles;

namespace DotVer.Application;

public sealed partial class GlobalJsonService : IGlobalJsonService
{
    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private const long MaxGlobalJsonBytes = 1024 * 1024;

    public GlobalJsonRequest? TryFind(string workingDirectory)
    {
        var candidate = FindFileUpward(workingDirectory);
        return candidate is null ? null : TryRead(candidate);
    }

    public GlobalJsonRequest? TryRead(string filePath) => Read(filePath).Request;

    public GlobalJsonReadResult Read(string filePath)
    {
        var candidate = Path.GetFullPath(filePath);
        if (!File.Exists(candidate))
        {
            return new GlobalJsonReadResult(GlobalJsonReadStatus.NoPin, null);
        }

        try
        {
            // Open without following links and validate the opened handle
            // itself (no check-then-open race): links and special files
            // (FIFO/device) are rejected instead of followed or blocked on.
            using var stream = OpenRegularFile(candidate);
            if (stream is null)
            {
                return new GlobalJsonReadResult(GlobalJsonReadStatus.Invalid, null);
            }

            var bytes = ReadBounded(stream);
            if (bytes is null)
            {
                return new GlobalJsonReadResult(GlobalJsonReadStatus.Invalid, null);
            }

            using var document = JsonDocument.Parse(bytes, DocumentOptions);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return new GlobalJsonReadResult(GlobalJsonReadStatus.Invalid, null);
            }

            if (!root.TryGetProperty("sdk", out var sdkNode))
            {
                return new GlobalJsonReadResult(GlobalJsonReadStatus.NoPin, null);
            }

            if (sdkNode.ValueKind != JsonValueKind.Object)
            {
                return new GlobalJsonReadResult(GlobalJsonReadStatus.Invalid, null);
            }

            if (!sdkNode.TryGetProperty("version", out var versionNode))
            {
                return new GlobalJsonReadResult(GlobalJsonReadStatus.NoPin, null);
            }

            if (versionNode.ValueKind != JsonValueKind.String ||
                !SdkVersion.TryParse(versionNode.GetString(), out var version))
            {
                return new GlobalJsonReadResult(GlobalJsonReadStatus.Invalid, null);
            }

            var rollForward = sdkNode.TryGetProperty("rollForward", out var rollForwardNode) &&
                              rollForwardNode.ValueKind == JsonValueKind.String
                ? ParseRollForward(rollForwardNode.GetString())
                : SdkRollForward.Default;

            return new GlobalJsonReadResult(
                GlobalJsonReadStatus.Pin,
                new GlobalJsonRequest(version!, rollForward, candidate));
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException or EntryPointNotFoundException or DllNotFoundException)
        {
            // A malformed global.json must not break every intercepted dotnet call.
            return new GlobalJsonReadResult(GlobalJsonReadStatus.Invalid, null);
        }
    }

    private static FileStream? OpenRegularFile(string path) =>
        OperatingSystem.IsWindows() ? OpenRegularFileWindows(path) : OpenRegularFileUnix(path);

    private static FileStream? OpenRegularFileWindows(string path)
    {
        const uint genericRead = 0x80000000;
        const uint fileShareRead = 0x00000001;
        const uint openExisting = 3;
        const uint fileFlagOpenReparsePoint = 0x00200000;
        const uint fileAttributeReparsePoint = 0x00000400;

        var handle = CreateFileW(path, genericRead, fileShareRead, 0, openExisting, fileFlagOpenReparsePoint, 0);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            return null;
        }

        if (!GetFileInformationByHandle(handle, out var info) ||
            (info.FileAttributes & fileAttributeReparsePoint) != 0 ||
            (info.FileAttributes & (uint)FileAttributes.Directory) != 0)
        {
            handle.Dispose();
            return null;
        }

        return new FileStream(handle, FileAccess.Read);
    }

    private static FileStream? OpenRegularFileUnix(string path)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            // Unknown Unix platform: fail closed instead of guessing layouts.
            return null;
        }

        // O_RDONLY | O_NOFOLLOW | O_NONBLOCK | O_CLOEXEC (values differ per platform).
        var flags = OperatingSystem.IsMacOS()
            ? 0x0100 | 0x0004 | 0x1000000
            : 0x20000 | 0x0800 | 0x80000;

        var fd = UnixOpen(path, flags);
        if (fd < 0)
        {
            return null;
        }

        SafeFileHandle? handle = null;
        try
        {
            if (!IsRegularFileDescriptor(fd))
            {
                _ = UnixClose(fd);
                fd = -1;
                return null;
            }

            handle = new SafeFileHandle((IntPtr)fd, ownsHandle: true);
            fd = -1;
            var stream = new FileStream(handle, FileAccess.Read);
            handle = null;
            return stream;
        }
        catch
        {
            handle?.Dispose();
            if (fd >= 0)
            {
                _ = UnixClose(fd);
            }

            throw;
        }
    }

    private static bool IsRegularFileDescriptor(int fd)
    {
        const uint sIfmt = 0xF000;
        const uint sIfReg = 0x8000;

        if (OperatingSystem.IsLinux())
        {
            // struct statx has a fixed layout on every Linux architecture:
            // stx_mode is a ushort at offset 28. AT_EMPTY_PATH stats the fd.
            const int atSymlinkNoFollow = 0x0100;
            const int atEmptyPath = 0x1000;
            const uint statxTypeAndMode = 0x0003;
            var statx = new byte[256];
            return LinuxStatx(fd, string.Empty, atSymlinkNoFollow | atEmptyPath, statxTypeAndMode, statx) == 0 &&
                   (BitConverter.ToUInt16(statx, 28) & sIfmt) == sIfReg;
        }

        // macOS INODE64 stat layout: st_dev (int32) at 0, st_mode (ushort) at 4.
        // x64 needs the $INODE64 symbol explicitly; arm64 only ships INODE64.
        var stat = new byte[144];
        var result = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => MacInode64Fstat(fd, stat),
            Architecture.Arm64 => UnixFstat(fd, stat),
            _ => -1
        };

        return result == 0 && (BitConverter.ToUInt16(stat, 4) & sIfmt) == sIfReg;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial SafeFileHandle CreateFileW(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        nint securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        nint templateFile);

    [LibraryImport("kernel32.dll", EntryPoint = "GetFileInformationByHandle", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFileInformationByHandle(SafeFileHandle handle, out ByHandleFileInformation info);

    [LibraryImport("libc", EntryPoint = "open", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int UnixOpen(string pathname, int flags);

    [LibraryImport("libc", EntryPoint = "fstat", SetLastError = true)]
    private static partial int UnixFstat(int fd, byte[] buffer);

    [LibraryImport("libc", EntryPoint = "fstat$INODE64", SetLastError = true)]
    private static partial int MacInode64Fstat(int fd, byte[] buffer);

    [LibraryImport("libc", EntryPoint = "statx", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int LinuxStatx(int directoryFd, string pathname, int flags, uint mask, byte[] buffer);

    [LibraryImport("libc", EntryPoint = "close", SetLastError = true)]
    private static partial int UnixClose(int fd);

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public long CreationTime;
        public long LastAccessTime;
        public long LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    private static byte[]? ReadBounded(Stream stream)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
        {
            if (buffer.Length + read > MaxGlobalJsonBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    public GlobalJsonWriteResult WritePin(string directory, SdkVersion version, SdkRollForward? rollForward)
    {
        ArgumentNullException.ThrowIfNull(version);

        var fullDirectory = Path.GetFullPath(directory);
        if (!Directory.Exists(fullDirectory))
        {
            throw new DirectoryNotFoundException($"Directory '{fullDirectory}' does not exist.");
        }

        var target = Path.Combine(fullDirectory, "global.json");
        var existed = File.Exists(target);
        var root = existed ? ReadRootObject(target) : new JsonObject();

        JsonObject sdk;
        if (root["sdk"] is JsonObject existingSdk)
        {
            sdk = existingSdk;
        }
        else if (root["sdk"] is null)
        {
            sdk = new JsonObject();
            root["sdk"] = sdk;
        }
        else
        {
            throw new InvalidOperationException($"The 'sdk' property in '{target}' is not a JSON object; refusing to overwrite it.");
        }

        sdk["version"] = version.Raw;
        if (rollForward is null)
        {
            sdk.Remove("rollForward");
        }
        else
        {
            sdk["rollForward"] = FormatRollForward(rollForward.Value);
        }

        WriteAtomically(target, root.ToJsonString(WriteOptions) + Environment.NewLine);
        return new GlobalJsonWriteResult(target, version.Raw, rollForward, existed, Cleared: false, FileDeleted: false);
    }

    public GlobalJsonWriteResult ClearPin(string workingDirectory, bool allowParent = false)
    {
        var fullDirectory = Path.GetFullPath(workingDirectory);
        var localTarget = Path.Combine(fullDirectory, "global.json");
        string target;
        if (File.Exists(localTarget))
        {
            target = localTarget;
        }
        else if (allowParent)
        {
            target = FindFileUpward(fullDirectory)
                ?? throw new InvalidOperationException($"No global.json found at or above '{fullDirectory}'.");
        }
        else
        {
            var parent = FindFileUpward(fullDirectory);
            throw new InvalidOperationException(parent is null
                ? $"No global.json found in '{fullDirectory}'."
                : $"No global.json in '{fullDirectory}'; the effective pin lives at '{parent}'. Re-run with --parent to modify that file.");
        }

        var root = ReadRootObject(target);
        if (root["sdk"] is not JsonObject sdk)
        {
            return new GlobalJsonWriteResult(target, null, null, FileExisted: true, Cleared: false, FileDeleted: false);
        }

        var hadPin = sdk.Remove("version");
        var hadRollForward = sdk.Remove("rollForward");
        if (!hadPin && !hadRollForward)
        {
            // Nothing to clear: leave the file byte-for-byte untouched.
            return new GlobalJsonWriteResult(target, null, null, FileExisted: true, Cleared: false, FileDeleted: false);
        }

        if (sdk.Count == 0)
        {
            root.Remove("sdk");
        }

        if (root.Count == 0)
        {
            File.Delete(target);
            return new GlobalJsonWriteResult(target, null, null, FileExisted: true, Cleared: hadPin, FileDeleted: true);
        }

        WriteAtomically(target, root.ToJsonString(WriteOptions) + Environment.NewLine);
        return new GlobalJsonWriteResult(target, null, null, FileExisted: true, Cleared: hadPin, FileDeleted: false);
    }

    public static bool TryParseRollForward(string? raw, out SdkRollForward rollForward)
    {
        rollForward = raw?.Trim().ToLowerInvariant() switch
        {
            "disable" => SdkRollForward.Disable,
            "patch" => SdkRollForward.Patch,
            "feature" => SdkRollForward.Feature,
            "minor" => SdkRollForward.Minor,
            "major" => SdkRollForward.Major,
            "latestpatch" => SdkRollForward.LatestPatch,
            "latestfeature" => SdkRollForward.LatestFeature,
            "latestminor" => SdkRollForward.LatestMinor,
            "latestmajor" => SdkRollForward.LatestMajor,
            _ => SdkRollForward.Default
        };

        return rollForward != SdkRollForward.Default;
    }

    public static string FormatRollForward(SdkRollForward rollForward) =>
        rollForward switch
        {
            SdkRollForward.Disable => "disable",
            SdkRollForward.Patch => "patch",
            SdkRollForward.Feature => "feature",
            SdkRollForward.Minor => "minor",
            SdkRollForward.Major => "major",
            SdkRollForward.LatestPatch => "latestPatch",
            SdkRollForward.LatestFeature => "latestFeature",
            SdkRollForward.LatestMinor => "latestMinor",
            SdkRollForward.LatestMajor => "latestMajor",
            _ => throw new ArgumentOutOfRangeException(nameof(rollForward), rollForward, "The default roll-forward is expressed by omitting 'rollForward'.")
        };

    private static SdkRollForward ParseRollForward(string? raw) =>
        TryParseRollForward(raw, out var rollForward) ? rollForward : SdkRollForward.Default;

    private static string? FindFileUpward(string workingDirectory)
    {
        var cursor = new DirectoryInfo(workingDirectory);
        while (cursor is not null)
        {
            var candidate = Path.Combine(cursor.FullName, "global.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            cursor = cursor.Parent;
        }

        return null;
    }

    private static JsonObject ReadRootObject(string path)
    {
        if (new FileInfo(path).Length > MaxGlobalJsonBytes)
        {
            throw new InvalidOperationException($"'{path}' exceeds the {MaxGlobalJsonBytes}-byte limit for global.json; refusing to modify it.");
        }

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(File.ReadAllText(path), documentOptions: DocumentOptions);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"'{path}' is not valid JSON; refusing to overwrite it. {exception.Message}", exception);
        }

        return node as JsonObject
            ?? throw new InvalidOperationException($"'{path}' does not contain a JSON object; refusing to overwrite it.");
    }

    private static void WriteAtomically(string target, string content)
    {
        var directory = Path.GetDirectoryName(target)!;
        var temp = Path.Combine(directory, $".global.json.{Guid.NewGuid():N}.tmp");
        try
        {
            // CreateNew: never write through a pre-planted file/symlink at the temp path.
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(content);
            }

            // Move replaces the target itself (a symlink at target is replaced, not followed).
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
}
