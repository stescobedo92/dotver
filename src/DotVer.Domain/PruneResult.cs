namespace DotVer.Domain;

public sealed record PruneEntry(
    string Version,
    string Channel,
    long SizeBytes,
    string HostRoot,
    PruneAction Action,
    string? Reason,
    string? Architecture = null);

public sealed record PruneResult(
    IReadOnlyList<PruneEntry> Entries,
    long TotalBytes,
    long ReclaimableBytes,
    bool DryRun);
