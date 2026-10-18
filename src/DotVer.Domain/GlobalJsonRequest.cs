namespace DotVer.Domain;

public sealed record GlobalJsonRequest(SdkVersion Version, SdkRollForward RollForward, string? FilePath = null);
