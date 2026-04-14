namespace DotVer.Domain;

public sealed record EnvironmentBootstrapResult(
    string ShimsDirectory,
    bool PersistentPathUpdated,
    bool ProcessPathUpdated,
    IReadOnlyList<string> ProfilesUpdated,
    IReadOnlyList<string> Notes);
