namespace DotVer.Domain;

public sealed record GlobalJsonWriteResult(
    string FilePath,
    string? Version,
    SdkRollForward? RollForward,
    bool FileExisted,
    bool Cleared,
    bool FileDeleted);
