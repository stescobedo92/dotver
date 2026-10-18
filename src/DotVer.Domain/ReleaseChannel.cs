namespace DotVer.Domain;

public sealed record ReleaseChannel(
    string Channel,
    string LatestSdkVersion,
    string SupportPhase,
    string ReleaseType,
    string? ReleasesJsonUrl = null);
