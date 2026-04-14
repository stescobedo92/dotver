namespace DotVer.Domain;

public sealed record SdkInstallation(
    SdkVersion Version,
    string HostRoot,
    string SdkRoot,
    string DotnetPath,
    SdkOriginKind Origin,
    string OriginHint,
    bool IsManaged,
    bool CanBeUsedDirectly,
    string DiscoverySource);
