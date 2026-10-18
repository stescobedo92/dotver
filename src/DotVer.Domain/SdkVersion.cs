using System.Text.RegularExpressions;

namespace DotVer.Domain;

public sealed record SdkVersion(string Raw, int Major, int Minor, int FeatureBand, int Patch, string? Suffix = null) : IComparable<SdkVersion>
{
    // Suffix is strict SemVer (identifiers of [0-9A-Za-z-], dot separated).
    // Raw is used in file paths, so separators/traversal/control chars must never parse.
    private static readonly Regex Pattern = new(
        @"^(?<major>\d+)\.(?<minor>\d+)\.(?<feature>\d+)(?:[-+](?<suffix>[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private const int MaxRawLength = 128;

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
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > MaxRawLength)
        {
            return false;
        }

        var match = Pattern.Match(value.Trim());
        if (!match.Success)
        {
            return false;
        }

        // \d+ is unbounded: TryParse so an overflowing component fails closed
        // instead of throwing OverflowException on untrusted input.
        if (!int.TryParse(match.Groups["major"].Value, out var major) ||
            !int.TryParse(match.Groups["minor"].Value, out var minor) ||
            !int.TryParse(match.Groups["feature"].Value, out var featureBandComponent))
        {
            return false;
        }
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
