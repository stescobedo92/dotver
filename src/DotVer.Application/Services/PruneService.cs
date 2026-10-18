using DotVer.Domain;

namespace DotVer.Application;

public sealed class PruneService : IPruneService
{
    private readonly IInventoryService _inventoryService;
    private readonly IStateService _stateService;
    private readonly IGlobalJsonService _globalJsonService;
    private readonly IEnvironmentContext _environment;

    public PruneService(
        IInventoryService inventoryService,
        IStateService stateService,
        IGlobalJsonService globalJsonService,
        IEnvironmentContext environment)
    {
        _inventoryService = inventoryService;
        _stateService = stateService;
        _globalJsonService = globalJsonService;
        _environment = environment;
    }

    public async Task<PruneResult> PruneAsync(bool keepLatestPerChannel, bool execute, CancellationToken cancellationToken)
    {
        var inventory = await _inventoryService.ScanAsync(cancellationToken).ConfigureAwait(false);
        var config = await _stateService.GetConfigAsync(cancellationToken).ConfigureAwait(false);
        var managed = inventory.Where(static sdk => sdk.IsManaged).ToList();

        var latestByChannel = managed
            .GroupBy(static sdk => ChannelOf(sdk), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group => group.OrderByDescending(static sdk => sdk.Version).First().Version.Raw,
                StringComparer.OrdinalIgnoreCase);

        var pin = _globalJsonService.TryFind(_environment.CurrentDirectory);
        var pinMatchedVersion = pin is null ? null : GlobalJsonMatcher.Match(pin, inventory)?.Version.Raw;

        var entries = new List<PruneEntry>();
        long total = 0;
        long reclaimable = 0;

        foreach (var sdk in managed.OrderByDescending(static sdk => sdk.Version))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var size = ComputeSizeBytes(sdk.HostRoot, cancellationToken);
            total += size;
            var channel = ChannelOf(sdk);
            var version = sdk.Version.Raw;

            var protectedReason = ProtectedReason(config, pin, pinMatchedVersion, version);

            var isLatest = string.Equals(latestByChannel[channel], version, StringComparison.OrdinalIgnoreCase);
            if (!keepLatestPerChannel || isLatest)
            {
                entries.Add(new PruneEntry(version, channel, size, sdk.HostRoot, PruneAction.Keep,
                    isLatest && keepLatestPerChannel ? "latest in channel" : null));
                continue;
            }

            if (protectedReason is not null)
            {
                entries.Add(new PruneEntry(version, channel, size, sdk.HostRoot, PruneAction.Skipped, protectedReason));
                continue;
            }

            reclaimable += size;
            if (!execute)
            {
                entries.Add(new PruneEntry(version, channel, size, sdk.HostRoot, PruneAction.WouldRemove, null));
                continue;
            }

            try
            {
                // Re-validate against fresh config/pin right before deleting:
                // another terminal may have pointed current/default/an alias at
                // this version since the initial snapshot. A residual race with
                // the delete itself remains (no cross-process lock in dotver).
                var freshConfig = await _stateService.GetConfigAsync(cancellationToken).ConfigureAwait(false);
                var freshPin = _globalJsonService.TryFind(_environment.CurrentDirectory);
                var freshMatched = freshPin is null ? null : GlobalJsonMatcher.Match(freshPin, inventory)?.Version.Raw;
                var nowProtected = ProtectedReason(freshConfig, freshPin, freshMatched, version);
                if (nowProtected is not null)
                {
                    reclaimable -= size;
                    entries.Add(new PruneEntry(version, channel, size, sdk.HostRoot, PruneAction.Skipped, nowProtected));
                    continue;
                }

                await _stateService.UninstallManagedAsync(version, cancellationToken).ConfigureAwait(false);
                entries.Add(new PruneEntry(version, channel, size, sdk.HostRoot, PruneAction.Removed, null));
            }
            catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                entries.Add(new PruneEntry(version, channel, size, sdk.HostRoot, PruneAction.Failed, Sanitize(exception.Message)));
            }
        }

        return new PruneResult(entries, total, reclaimable, DryRun: !execute);
    }

    private static string? ProtectedReason(
        DotVerConfig config,
        GlobalJsonRequest? pin,
        string? pinMatchedVersion,
        string version)
    {
        if (string.Equals(config.CurrentVersion, version, StringComparison.OrdinalIgnoreCase))
        {
            return "current version";
        }

        if (string.Equals(config.DefaultVersion, version, StringComparison.OrdinalIgnoreCase))
        {
            return "default version";
        }

        if (config.Aliases.Values.Contains(version, StringComparer.OrdinalIgnoreCase))
        {
            return "targeted by an alias";
        }

        if (pin is not null &&
            (string.Equals(pin.Version.Raw, version, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(pinMatchedVersion, version, StringComparison.OrdinalIgnoreCase)))
        {
            return "pinned by global.json";
        }

        return null;
    }

    private static string ChannelOf(SdkInstallation sdk) => $"{sdk.Version.Major}.{sdk.Version.Minor}";

    private static string Sanitize(string value) =>
        new(value.Where(static c => !char.IsControl(c)).ToArray());

    private static long ComputeSizeBytes(string root, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(root))
        {
            return 0;
        }

        // Never measure through a linked install root itself.
        var rootInfo = new DirectoryInfo(root);
        if (rootInfo.LinkTarget is not null || (rootInfo.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            return 0;
        }

        long total = 0;
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            // Never follow links/junctions while measuring.
            AttributesToSkip = FileAttributes.ReparsePoint
        };

        try
        {
            foreach (var file in Directory.EnumerateFiles(root, "*", options))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    total += new FileInfo(file).Length;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // A vanishing/locked file must not fail the whole report.
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return total;
        }

        return total;
    }
}
