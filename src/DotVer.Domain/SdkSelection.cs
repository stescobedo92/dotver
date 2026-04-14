namespace DotVer.Domain;

public sealed record SdkSelection(SdkInstallation? Installation, string Reason, bool FromGlobalJson);
