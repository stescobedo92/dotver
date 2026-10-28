namespace DotVer.Domain;

public sealed record WorkspacePinEntry(
    string FilePath,
    string? RequestedVersion,
    string? RollForward,
    string? MatchedVersion,
    bool Satisfied,
    bool Invalid = false);

public sealed record WorkspaceScanResult(
    string Root,
    IReadOnlyList<WorkspacePinEntry> Entries,
    bool Truncated);

public sealed record WorkspaceEnsureResult(
    WorkspaceScanResult Scan,
    IReadOnlyList<string> InstalledVersions,
    IReadOnlyList<string> Errors);

/// <summary>What <c>scan --ensure --dry-run</c> would do: nothing is installed.</summary>
public sealed record WorkspaceEnsurePlan(
    WorkspaceScanResult Scan,
    IReadOnlyList<string> MissingVersions,
    IReadOnlyList<string> Errors);
