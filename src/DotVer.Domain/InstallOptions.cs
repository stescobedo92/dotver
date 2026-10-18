namespace DotVer.Domain;

public sealed record InstallOptions(string? Architecture = null);

public sealed record RuntimeInstallOutcome(
    string Runtime,
    string Version,
    string HostRoot,
    IReadOnlyList<string> InstalledVersions,
    bool WasAlreadyInstalled);
