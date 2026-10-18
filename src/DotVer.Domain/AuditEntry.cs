namespace DotVer.Domain;

public sealed record AuditEntry(
    SdkInstallation Installation,
    string Channel,
    bool MetadataKnown,
    bool IsVulnerable,
    string? FixedInSdk,
    IReadOnlyList<string> CveIds);
