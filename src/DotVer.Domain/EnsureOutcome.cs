namespace DotVer.Domain;

public sealed record EnsureOutcome(GlobalJsonRequest Request, SdkInstallation Installation, bool InstalledNow);
