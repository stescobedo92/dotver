namespace DotVer.Domain;

/// <summary>A published dotver release: its tag, version and asset download URLs by asset name.</summary>
public sealed record ReleaseInfo(string Tag, string Version, IReadOnlyDictionary<string, string> AssetUrls);

public sealed record SelfUpdateCheck(
    string CurrentVersion,
    string LatestVersion,
    string Tag,
    bool UpdateAvailable,
    string? AssetName,
    string ExecutablePath,
    bool CanSelfUpdate,
    string? Reason);

public sealed record SelfUpdateOutcome(
    string FromVersion,
    string ToVersion,
    string ExecutablePath,
    string AssetName,
    string? Sha256,
    bool Updated,
    string? Note);

public sealed record EnvironmentRemovalResult(
    IReadOnlyList<string> ProfilesCleaned,
    bool UserPathUpdated,
    bool UserVariablesRemoved,
    IReadOnlyList<string> Notes);

public sealed record SelfUninstallPlan(
    IReadOnlyList<string> ProfilesToClean,
    IReadOnlyList<string> DirectoriesToDelete,
    IReadOnlyList<string> FilesToDelete,
    bool RemoveUserPathEntry,
    bool RemoveUserVariables,
    string BinaryPath,
    string BinaryRemovalHint);

public sealed record SelfUninstallOutcome(
    SelfUninstallPlan Plan,
    bool Executed,
    EnvironmentRemovalResult? Environment,
    IReadOnlyList<string> Deleted,
    IReadOnlyList<string> Errors);
