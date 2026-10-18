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
