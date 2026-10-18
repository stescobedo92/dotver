using DotVer.Domain;

namespace DotVer.Application;

/// <summary>Applies global.json roll-forward rules against a local inventory.</summary>
public static class GlobalJsonMatcher
{
    public static SdkInstallation? Match(
        GlobalJsonRequest request,
        IReadOnlyList<SdkInstallation> installations,
        string? hostArchitecture = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(installations);

        // Automatic pin matching must never select a foreign-architecture
        // install: callers pass the host architecture and the pool is
        // restricted to host-matching SDKs. This includes prune's pin
        // protection: an arch-blind pool can resolve the pin to a different
        // (foreign, higher) version and leave the host build the pin actually
        // resolves to unprotected. Only omit hostArchitecture when the caller
        // genuinely wants any-architecture semantics.
        var pool = hostArchitecture is null
            ? installations
            : installations.Where(sdk => Architectures.MatchesHost(sdk.Architecture, hostArchitecture)).ToList();

        var ordered = pool.OrderByDescending(static sdk => sdk.Version).ToList();
        var exact = ordered.FirstOrDefault(sdk => sdk.Version.CompareTo(request.Version) == 0);
        if (request.RollForward is SdkRollForward.Default or SdkRollForward.Disable)
        {
            return exact;
        }

        if (exact is not null)
        {
            return exact;
        }

        // Roll-forward never rolls backwards: every candidate must be >= the pin.
        return request.RollForward switch
        {
            SdkRollForward.Patch or SdkRollForward.LatestPatch => ordered.FirstOrDefault(sdk =>
                sdk.Version.CompareTo(request.Version) >= 0 &&
                sdk.Version.Major == request.Version.Major &&
                sdk.Version.Minor == request.Version.Minor &&
                sdk.Version.FeatureBand == request.Version.FeatureBand),
            SdkRollForward.Feature or SdkRollForward.LatestFeature => ordered.FirstOrDefault(sdk =>
                sdk.Version.CompareTo(request.Version) >= 0 &&
                sdk.Version.Major == request.Version.Major &&
                sdk.Version.Minor == request.Version.Minor &&
                sdk.Version.FeatureBand >= request.Version.FeatureBand),
            SdkRollForward.Minor or SdkRollForward.LatestMinor => ordered.FirstOrDefault(sdk =>
                sdk.Version.CompareTo(request.Version) >= 0 &&
                sdk.Version.Major == request.Version.Major &&
                sdk.Version.Minor >= request.Version.Minor),
            SdkRollForward.Major or SdkRollForward.LatestMajor => ordered.FirstOrDefault(sdk =>
                sdk.Version.CompareTo(request.Version) >= 0 &&
                sdk.Version.Major >= request.Version.Major),
            _ => exact
        };
    }
}
