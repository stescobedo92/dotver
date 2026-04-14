namespace DotVer.Domain;

public sealed record UninstallOutcome(
    string RemovedVersion,
    bool RemovedDirectory,
    string? NewCurrentVersion,
    string? NewDefaultVersion,
    IReadOnlyList<string> RemovedAliases);
