namespace DotVer.Domain;

public sealed record RuntimeInfo(string Kind, IReadOnlyList<string> Versions);

public sealed record SdkInfo(
    SdkInstallation Installation,
    IReadOnlyList<RuntimeInfo> Runtimes,
    IReadOnlyList<string> Workloads);
