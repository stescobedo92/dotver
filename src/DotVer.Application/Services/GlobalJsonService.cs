using System.Text.Json;
using DotVer.Domain;

namespace DotVer.Application;

public sealed class GlobalJsonService : IGlobalJsonService
{
    public GlobalJsonRequest? TryFind(string workingDirectory)
    {
        var cursor = new DirectoryInfo(workingDirectory);
        while (cursor is not null)
        {
            var candidate = Path.Combine(cursor.FullName, "global.json");
            if (File.Exists(candidate))
            {
                using var stream = File.OpenRead(candidate);
                using var document = JsonDocument.Parse(stream);
                if (!document.RootElement.TryGetProperty("sdk", out var sdkNode))
                {
                    return null;
                }

                if (!sdkNode.TryGetProperty("version", out var versionNode) ||
                    !SdkVersion.TryParse(versionNode.GetString(), out var version))
                {
                    return null;
                }

                var rollForward = sdkNode.TryGetProperty("rollForward", out var rollForwardNode)
                    ? ParseRollForward(rollForwardNode.GetString())
                    : SdkRollForward.Default;

                return new GlobalJsonRequest(version!, rollForward);
            }

            cursor = cursor.Parent;
        }

        return null;
    }

    private static SdkRollForward ParseRollForward(string? raw) =>
        raw?.Trim().ToLowerInvariant() switch
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
}
