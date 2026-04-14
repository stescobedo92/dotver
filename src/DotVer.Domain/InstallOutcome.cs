namespace DotVer.Domain;

public sealed record InstallOutcome(InstallPlan Plan, SdkInstallation Installation, bool WasAlreadyInstalled);
