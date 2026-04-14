namespace DotVer.Domain;

public enum SdkOriginKind
{
    ManagedInstall,
    OfficialInstallRoot,
    CustomPath,
    RegisteredRoot,
    PathProbe,
    Unknown
}
