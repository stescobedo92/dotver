namespace DotVer.Domain;

public enum SdkRollForward
{
    Default,
    Disable,
    Patch,
    Feature,
    Minor,
    Major,
    LatestPatch,
    LatestFeature,
    LatestMinor,
    LatestMajor
}
