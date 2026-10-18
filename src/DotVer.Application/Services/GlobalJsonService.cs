using System.Text.Json;
using System.Text.Json.Nodes;
using DotVer.Domain;

namespace DotVer.Application;

public sealed class GlobalJsonService : IGlobalJsonService
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
        if (candidate is null)
        {
            return null;
        }

        try
        {
            if (new FileInfo(candidate).Length > MaxGlobalJsonBytes)
            {
                return null;
            }

            using var stream = File.OpenRead(candidate);
            using var document = JsonDocument.Parse(stream, DocumentOptions);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("sdk", out var sdkNode) ||
                sdkNode.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!sdkNode.TryGetProperty("version", out var versionNode) ||
                versionNode.ValueKind != JsonValueKind.String ||
                !SdkVersion.TryParse(versionNode.GetString(), out var version))
            {
                return null;
            }

            var rollForward = sdkNode.TryGetProperty("rollForward", out var rollForwardNode) &&
                              rollForwardNode.ValueKind == JsonValueKind.String
                ? ParseRollForward(rollForwardNode.GetString())
                : SdkRollForward.Default;

            return new GlobalJsonRequest(version!, rollForward, candidate);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // A malformed global.json must not break every intercepted dotnet call.
            return null;
        }
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
