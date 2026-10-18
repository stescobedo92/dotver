namespace DotVer.Domain;

public sealed record OutdatedEntry(
    SdkInstallation Installation,
    string Channel,
    string? LatestInChannel,
    string? SupportPhase,
    string? ReleaseType,
    bool UpdateAvailable,
    bool IsEndOfLife,
    bool IsPrerelease)
{
    public bool NeedsAttention => UpdateAvailable || IsEndOfLife || IsPrerelease;
}
