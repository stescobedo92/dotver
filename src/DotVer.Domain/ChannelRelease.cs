namespace DotVer.Domain;

public sealed record ChannelRelease(
    string ReleaseVersion,
    bool IsSecurity,
    IReadOnlyList<string> SdkVersions,
    IReadOnlyList<string> CveIds);
