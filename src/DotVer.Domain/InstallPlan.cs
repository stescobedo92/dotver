namespace DotVer.Domain;

public sealed record InstallPlan(string RequestedToken, string ResolvedVersion, string Reason, ReleaseChannel? Channel);
