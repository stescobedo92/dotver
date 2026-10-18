using DotVer.Domain;

namespace DotVer.Application;

/// <summary>Applies global.json roll-forward rules against a local inventory.</summary>
public static class GlobalJsonMatcher
{
    public static SdkInstallation? Match(GlobalJsonRequest request, IReadOnlyList<SdkInstallation> installations)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(installations);

        var ordered = installations.OrderByDescending(static sdk => sdk.Version).ToList();
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
