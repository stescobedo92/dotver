using DotVer.Domain;

namespace DotVer.Application;

public sealed class AuditService : IAuditService
{
    private readonly IInventoryService _inventoryService;
    private readonly IReleaseMetadataService _metadataService;

    public AuditService(IInventoryService inventoryService, IReleaseMetadataService metadataService)
    {
        _inventoryService = inventoryService;
        _metadataService = metadataService;
    }

    public async Task<IReadOnlyList<AuditEntry>> GetAuditAsync(CancellationToken cancellationToken)
    {
        var inventory = await _inventoryService.ScanAsync(cancellationToken).ConfigureAwait(false);
        var releasesByChannel = new Dictionary<string, IReadOnlyList<ChannelRelease>>(StringComparer.OrdinalIgnoreCase);

        foreach (var channel in inventory.Select(static sdk => ChannelOf(sdk)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            releasesByChannel[channel] = await _metadataService
                .ListChannelReleasesAsync(channel, cancellationToken)
                .ConfigureAwait(false);
        }

        var entries = new List<AuditEntry>();
        foreach (var sdk in inventory)
        {
            entries.Add(Audit(sdk, releasesByChannel[ChannelOf(sdk)]));
        }

        return entries;
    }

    private static AuditEntry Audit(SdkInstallation sdk, IReadOnlyList<ChannelRelease> releases)
    {
        var parsed = releases
            .Select((release, index) => new ParsedRelease(
                release,
                index,
                ParseReleaseOrder(release.ReleaseVersion),
                release.SdkVersions
                    .Select(raw => SdkVersion.TryParse(raw, out var v) ? v : null)
                    .Where(static v => v is not null)
                    .Select(static v => v!)
                    .ToList()))
            .ToList();

        // The newest release that shipped this exact SDK is its origin: every
        // security fix up to that release is already included, and every
        // security release after it is missing — even if later releases no
        // longer publish an SDK for this feature band (band retired).
        var origin = parsed
            .Where(release => release.Sdks.Any(v => v.CompareTo(sdk.Version) == 0))
            .OrderByDescending(static release => release.Order)
            .ThenBy(static release => release.Index)
            .FirstOrDefault();

        if (origin is null)
        {
            return new AuditEntry(sdk, ChannelOf(sdk), MetadataKnown: false, IsVulnerable: false, FixedInSdk: null, []);
        }

        var securityAfter = parsed
            .Where(release => release.Release.IsSecurity && IsAfter(release, origin))
            .ToList();
        if (securityAfter.Count == 0)
        {
            return new AuditEntry(sdk, ChannelOf(sdk), MetadataKnown: true, IsVulnerable: false, FixedInSdk: null, []);
        }

        var cves = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var release in securityAfter)
        {
            foreach (var cve in release.Release.CveIds)
            {
                cves.Add(cve);
            }
        }

        var sameBandFixes = securityAfter
            .SelectMany(static release => release.Sdks)
            .Where(v => v.FeatureBand == sdk.Version.FeatureBand && v.CompareTo(sdk.Version) > 0)
            .ToList();

        string? fixedIn;
        if (sameBandFixes.Count > 0)
        {
            // The newest security SDK of the band carries every listed fix.
            fixedIn = sameBandFixes.Max()!.Raw;
        }
        else
        {
            // Band retired: recommend the newest security release's SDK
            // (a cross-band upgrade via `dotver upgrade`).
            var newestSecurity = securityAfter
                .OrderByDescending(static release => release.Order)
                .ThenBy(static release => release.Index)
                .First();
            fixedIn = newestSecurity.Sdks.Count == 0 ? null : newestSecurity.Sdks.Max()!.Raw;
        }

        return new AuditEntry(sdk, ChannelOf(sdk), MetadataKnown: true, IsVulnerable: true, fixedIn, [.. cves]);
    }

    private static bool IsAfter(ParsedRelease candidate, ParsedRelease origin)
    {
        var byOrder = candidate.Order.CompareTo(origin.Order);
        return byOrder != 0 ? byOrder > 0 : candidate.Index < origin.Index;
    }

    private static Version ParseReleaseOrder(string releaseVersion)
    {
        var core = releaseVersion.Split('-', '+')[0];
        return Version.TryParse(core, out var version) ? version : new Version(0, 0);
    }

    private static string ChannelOf(SdkInstallation sdk) => $"{sdk.Version.Major}.{sdk.Version.Minor}";

    private sealed record ParsedRelease(ChannelRelease Release, int Index, Version Order, List<SdkVersion> Sdks);
}
