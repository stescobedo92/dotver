namespace DotVer.Domain;

public sealed record MatrixEntry(
    string Token,
    string? Version,
    int? ExitCode,
    long DurationMs,
    string StandardOutput,
    string StandardError,
    string? Error);

public sealed record MatrixResult(IReadOnlyList<MatrixEntry> Entries)
{
    public bool AllSucceeded => Entries.All(static entry => entry.Error is null && entry.ExitCode == 0);
}
