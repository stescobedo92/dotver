using DotVer.Domain;

namespace DotVer.Application;

public sealed class OutdatedService : IOutdatedService
{
    private readonly IInventoryService _inventoryService;
    private readonly IInstallService _installService;
    private readonly IStateService _stateService;
    private readonly IVersionResolver _versionResolver;
    private readonly IGlobalJsonService _globalJsonService;
    private readonly IEnvironmentContext _environment;

    public OutdatedService(
        IInventoryService inventoryService,
        IInstallService installService,
        IStateService stateService,
        IVersionResolver versionResolver,
        IGlobalJsonService globalJsonService,
        IEnvironmentContext environment)
    {
        _inventoryService = inventoryService;
        _installService = installService;
        _stateService = stateService;
        _versionResolver = versionResolver;
        _globalJsonService = globalJsonService;
        _environment = environment;
    }

    public async Task<IReadOnlyList<OutdatedEntry>> GetOutdatedAsync(CancellationToken cancellationToken)
    {
        var inventory = await _inventoryService.ScanAsync(cancellationToken).ConfigureAwait(false);
        var channels = await _installService.ListRemoteAsync(cancellationToken).ConfigureAwait(false);
        var byChannel = channels
            .GroupBy(static channel => channel.Channel, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                // Duplicate channel entries: keep the one advertising the highest valid SDK.
                static group => group
                    .OrderByDescending(static channel => SdkVersion.TryParse(channel.LatestSdkVersion, out var v) ? v : null)
                    .First(),
                StringComparer.OrdinalIgnoreCase);

        var entries = new List<OutdatedEntry>();
        foreach (var sdk in inventory)
        {
            var channelKey = $"{sdk.Version.Major}.{sdk.Version.Minor}";
            byChannel.TryGetValue(channelKey, out var channel);

            string? latestRaw = null;
            var updateAvailable = false;
            if (channel is not null && SdkVersion.TryParse(channel.LatestSdkVersion, out var latest))
            {
                // Remote metadata must stay inside its own channel, and a stable
                // install is never auto-upgraded onto a prerelease.
                var sameChannel = latest!.Major == sdk.Version.Major && latest.Minor == sdk.Version.Minor;
                var prereleaseJump = !string.IsNullOrWhiteSpace(latest.Suffix) && string.IsNullOrWhiteSpace(sdk.Version.Suffix);
                if (sameChannel)
                {
                    latestRaw = latest.Raw;
                    updateAvailable = !prereleaseJump && latest.CompareTo(sdk.Version) > 0;
                }
            }

            entries.Add(new OutdatedEntry(
                sdk,
                channelKey,
                latestRaw,
                channel?.SupportPhase,
                channel?.ReleaseType,
                updateAvailable,
                IsEndOfLife: string.Equals(channel?.SupportPhase, "eol", StringComparison.OrdinalIgnoreCase),
                IsPrerelease: !string.IsNullOrWhiteSpace(sdk.Version.Suffix) ||
                              string.Equals(channel?.SupportPhase, "preview", StringComparison.OrdinalIgnoreCase)));
        }

        return entries;
    }

    public async Task<IReadOnlyList<UpgradeOutcome>> UpgradeAsync(
        string? token,
        bool all,
        bool pruneOld,
        CancellationToken cancellationToken)
    {
        if (all == !string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("Specify exactly one of a version token or --all.");
        }

        var entries = (await GetOutdatedAsync(cancellationToken).ConfigureAwait(false))
            .Where(static entry => entry.Installation.IsManaged)
            .ToList();

        List<OutdatedEntry> targets;
        if (all)
        {
            targets = entries.Where(static entry => entry.UpdateAvailable).ToList();
        }
        else
        {
            var config = await _stateService.GetConfigAsync(cancellationToken).ConfigureAwait(false);
            var inventory = entries.Select(static entry => entry.Installation).ToList();
            var resolved = _versionResolver.FindByToken(token!, config, inventory)
                ?? throw new InvalidOperationException($"No managed SDK found for '{token}'. Only dotver-managed SDKs can be upgraded.");
            targets = entries
                .Where(entry => string.Equals(entry.Installation.Version.Raw, resolved.Version.Raw, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        var outcomes = new List<UpgradeOutcome>();
        foreach (var target in targets)
        {
            try
            {
                outcomes.Add(await UpgradeOneAsync(target, pruneOld, cancellationToken).ConfigureAwait(false));
            }
            catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                // One failed SDK must not hide the results of the others.
                outcomes.Add(new UpgradeOutcome(
                    target.Channel,
                    target.Installation.Version.Raw,
                    target.LatestInChannel ?? target.Installation.Version.Raw,
                    Upgraded: false,
                    false,
                    false,
                    [],
                    PrunedOld: false,
                    Error: Sanitize(exception.Message)));
            }
        }

        return outcomes;
    }

    private async Task<UpgradeOutcome> UpgradeOneAsync(OutdatedEntry target, bool pruneOld, CancellationToken cancellationToken)
    {
        var from = target.Installation.Version.Raw;
        if (!target.UpdateAvailable || target.LatestInChannel is null)
        {
            return new UpgradeOutcome(target.Channel, from, from, Upgraded: false, false, false, [], PrunedOld: false);
        }

        var to = target.LatestInChannel;
        var installOutcome = await _installService.InstallAsync(to, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(installOutcome.Installation.Version.Raw, to, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Install resolved to {installOutcome.Installation.Version.Raw} instead of {to}; aborting repoint and prune.");
        }

        var repoint = await _stateService.RepointVersionAsync(from, to, cancellationToken).ConfigureAwait(false);

        var pruned = false;
        string? note = null;
        if (pruneOld && !string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
        {
            var pin = _globalJsonService.TryFind(_environment.CurrentDirectory);
            if (pin is not null && string.Equals(pin.Version.Raw, from, StringComparison.OrdinalIgnoreCase))
            {
                note = $"Prune skipped: {from} is pinned by global.json for this directory.";
            }
            else
            {
                await _stateService.UninstallManagedAsync(from, cancellationToken).ConfigureAwait(false);
                pruned = true;
            }
        }

        return new UpgradeOutcome(
            target.Channel,
            from,
            to,
            Upgraded: true,
            repoint.RepointedCurrent,
            repoint.RepointedDefault,
            repoint.RepointedAliases,
            pruned,
            Error: null,
            Note: note);
    }

    private static string Sanitize(string value) =>
        new(value.Where(static c => !char.IsControl(c)).ToArray());
}
