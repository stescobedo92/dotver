namespace DotVer.Domain;

public sealed record ShimInstallResult(string ShimsDirectory, IReadOnlyList<string> FilesWritten);
