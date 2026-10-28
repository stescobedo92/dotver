namespace DotVer.Domain;

public sealed record IdeSettingsResult(
    string FilePath,
    bool Created,
    IReadOnlyList<string> KeysWritten,
    bool CommentsDropped,
    string HostRoot);
