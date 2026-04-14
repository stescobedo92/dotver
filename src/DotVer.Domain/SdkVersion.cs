using System.Text.RegularExpressions;

namespace DotVer.Domain;

public sealed record SdkVersion(string Raw, int Major, int Minor, int FeatureBand, int Patch, string? Suffix = null) : IComparable<SdkVersion>
{
    private static readonly Regex Pattern = new(
        @"^(?<major>\d+)\.(?<minor>\d+)\.(?<feature>\d+)(?:[-+](?<suffix>.+))?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public int CompareTo(SdkVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        var numeric = Major.CompareTo(other.Major);
        if (numeric != 0)
        {
            return numeric;
        }

        numeric = Minor.CompareTo(other.Minor);
        if (numeric != 0)
        {
            return numeric;
        }

        numeric = FeatureBand.CompareTo(other.FeatureBand);
        if (numeric != 0)
        {
            return numeric;
        }

        numeric = Patch.CompareTo(other.Patch);
        if (numeric != 0)
        {
            return numeric;
        }

        if (string.IsNullOrWhiteSpace(Suffix) && string.IsNullOrWhiteSpace(other.Suffix))
        {
            return 0;
        }

        if (string.IsNullOrWhiteSpace(Suffix))
        {
            return 1;
        }

        if (string.IsNullOrWhiteSpace(other.Suffix))
        {
            return -1;
        }

        return string.Compare(Suffix, other.Suffix, StringComparison.OrdinalIgnoreCase);
    }

    public override string ToString() => Raw;

    public static bool TryParse(string? value, out SdkVersion? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var match = Pattern.Match(value.Trim());
        if (!match.Success)
        {
            return false;
        }

        var major = int.Parse(match.Groups["major"].Value);
        var minor = int.Parse(match.Groups["minor"].Value);
        var featureBandComponent = int.Parse(match.Groups["feature"].Value);
        var featureBand = featureBandComponent / 100 * 100;
        var patch = featureBandComponent % 100;

        version = new SdkVersion(
            value.Trim(),
            major,
            minor,
            featureBand,
            patch,
            match.Groups["suffix"].Success ? match.Groups["suffix"].Value : null);

        return true;
    }

    public static SdkVersion Parse(string value) =>
        TryParse(value, out var version)
            ? version!
            : throw new ArgumentException($"'{value}' is not a valid .NET SDK version.", nameof(value));
}
