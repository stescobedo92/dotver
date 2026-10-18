using System.Text.Json;
using System.Text.Json.Nodes;
using DotVer.Domain;

namespace DotVer.Application;

/// <summary>
/// Builds stable, machine-readable JSON reports for scripting and CI.
/// Built with <see cref="JsonObject"/> so every value is escaped by System.Text.Json.
/// </summary>
public static class JsonReports
{
    public const int SchemaVersion = 1;

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static string ForList(IReadOnlyList<SdkInstallation> sdks, SdkSelection selection, DotVerConfig config)
    {
        ArgumentNullException.ThrowIfNull(sdks);
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(config);

        var items = new JsonArray();
        foreach (var sdk in sdks)
        {
            items.Add(new JsonObject
            {
                ["version"] = sdk.Version.Raw,
                ["isCurrent"] = string.Equals(selection.Installation?.Version.Raw, sdk.Version.Raw, StringComparison.OrdinalIgnoreCase),
                ["isDefault"] = string.Equals(config.DefaultVersion, sdk.Version.Raw, StringComparison.OrdinalIgnoreCase),
                ["isManaged"] = sdk.IsManaged,
                ["canBeUsedDirectly"] = sdk.CanBeUsedDirectly,
                ["origin"] = sdk.Origin.ToString(),
                ["discoverySource"] = sdk.DiscoverySource,
                ["hostRoot"] = sdk.HostRoot,
                ["sdkRoot"] = sdk.SdkRoot,
                ["dotnetPath"] = sdk.DotnetPath
            });
        }

        return Root(new JsonObject { ["sdks"] = items });
    }

    public static string ForCurrent(SdkSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);

        return Root(new JsonObject
        {
            ["version"] = selection.Installation?.Version.Raw,
            ["reason"] = selection.Reason,
            ["fromGlobalJson"] = selection.FromGlobalJson,
            ["hostRoot"] = selection.Installation?.HostRoot,
            ["dotnetPath"] = selection.Installation?.DotnetPath
        });
    }

    public static string ForRemote(IReadOnlyList<ReleaseChannel> channels)
    {
        ArgumentNullException.ThrowIfNull(channels);

        var items = new JsonArray();
        foreach (var channel in channels)
        {
            items.Add(new JsonObject
            {
                ["channel"] = channel.Channel,
                ["latestSdk"] = channel.LatestSdkVersion,
                ["supportPhase"] = channel.SupportPhase,
                ["releaseType"] = channel.ReleaseType
            });
        }

        return Root(new JsonObject { ["channels"] = items });
    }

    public static string ForDoctor(
        IReadOnlyList<SdkInstallation> inventory,
        SdkSelection selection,
        DotVerConfig config,
        IReadOnlyList<string> warnings,
        DoctorResult? diagnosis = null)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(warnings);

        var aliases = new JsonObject();
        foreach (var pair in config.Aliases.OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            aliases[pair.Key] = pair.Value;
        }

        var roots = new JsonArray();
        foreach (var root in config.RegisteredRoots)
        {
            roots.Add(root);
        }

        var warningItems = new JsonArray();
        foreach (var warning in warnings)
        {
            warningItems.Add(warning);
        }

        var payload = new JsonObject
        {
            ["sdksDetected"] = inventory.Count,
            ["managedSdks"] = inventory.Count(static sdk => sdk.IsManaged),
            ["directSelectSdks"] = inventory.Count(static sdk => sdk.CanBeUsedDirectly),
            ["current"] = selection.Installation?.Version.Raw,
            ["currentReason"] = selection.Reason,
            ["default"] = config.DefaultVersion,
            ["aliases"] = aliases,
            ["registeredRoots"] = roots,
            ["warnings"] = warningItems
        };

        if (diagnosis is not null)
        {
            var issues = new JsonArray();
            foreach (var issue in diagnosis.Issues)
            {
                issues.Add(new JsonObject
                {
                    ["code"] = issue.Code,
                    ["message"] = issue.Message
                });
            }

            var fixes = new JsonArray();
            foreach (var fix in diagnosis.FixesApplied)
            {
                fixes.Add(fix);
            }

            payload["issues"] = issues;
            payload["fixesApplied"] = fixes;
        }

        return Root(payload);
    }

    public static string ForPrune(PruneResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var items = new JsonArray();
        foreach (var entry in result.Entries)
        {
            items.Add(new JsonObject
            {
                ["version"] = entry.Version,
                ["channel"] = entry.Channel,
                ["sizeBytes"] = entry.SizeBytes,
                ["hostRoot"] = entry.HostRoot,
                ["action"] = entry.Action.ToString(),
                ["reason"] = entry.Reason
            });
        }

        return Root(new JsonObject
        {
            ["dryRun"] = result.DryRun,
            ["totalBytes"] = result.TotalBytes,
            ["reclaimableBytes"] = result.ReclaimableBytes,
            ["sdks"] = items
        });
    }

    public static string ForLocal(string? filePath, string? version, string? rollForward, bool cleared, bool installed)
    {
        return Root(new JsonObject
        {
            ["filePath"] = filePath,
            ["version"] = version,
            ["rollForward"] = rollForward,
            ["cleared"] = cleared,
            ["installed"] = installed
        });
    }

    public static string ForEnsure(EnsureOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        return Root(new JsonObject
        {
            ["filePath"] = outcome.Request.FilePath,
            ["requested"] = outcome.Request.Version.Raw,
            ["rollForward"] = outcome.Request.RollForward == SdkRollForward.Default
                ? null
                : GlobalJsonService.FormatRollForward(outcome.Request.RollForward),
            ["resolved"] = outcome.Installation.Version.Raw,
            ["installedNow"] = outcome.InstalledNow,
            ["hostRoot"] = outcome.Installation.HostRoot,
            ["dotnetPath"] = outcome.Installation.DotnetPath
        });
    }

    public static string ForWhich(string? version, string? dotnetPath, string? hostRoot, string reason)
    {
        return Root(new JsonObject
        {
            ["version"] = version,
            ["dotnetPath"] = dotnetPath,
            ["hostRoot"] = hostRoot,
            ["reason"] = reason
        });
    }

    public static string ForOutdated(IReadOnlyList<OutdatedEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var items = new JsonArray();
        foreach (var entry in entries)
        {
            items.Add(new JsonObject
            {
                ["version"] = entry.Installation.Version.Raw,
                ["channel"] = entry.Channel,
                ["latestInChannel"] = entry.LatestInChannel,
                ["supportPhase"] = entry.SupportPhase,
                ["releaseType"] = entry.ReleaseType,
                ["updateAvailable"] = entry.UpdateAvailable,
                ["isEndOfLife"] = entry.IsEndOfLife,
                ["isPrerelease"] = entry.IsPrerelease,
                ["isManaged"] = entry.Installation.IsManaged,
                ["hostRoot"] = entry.Installation.HostRoot
            });
        }

        return Root(new JsonObject { ["sdks"] = items });
    }

    public static string ForUpgrade(IReadOnlyList<UpgradeOutcome> outcomes)
    {
        ArgumentNullException.ThrowIfNull(outcomes);

        var items = new JsonArray();
        foreach (var outcome in outcomes)
        {
            var aliases = new JsonArray();
            foreach (var alias in outcome.RepointedAliases)
            {
                aliases.Add(alias);
            }

            items.Add(new JsonObject
            {
                ["channel"] = outcome.Channel,
                ["from"] = outcome.FromVersion,
                ["to"] = outcome.ToVersion,
                ["upgraded"] = outcome.Upgraded,
                ["repointedCurrent"] = outcome.RepointedCurrent,
                ["repointedDefault"] = outcome.RepointedDefault,
                ["repointedAliases"] = aliases,
                ["prunedOld"] = outcome.PrunedOld,
                ["error"] = outcome.Error,
                ["note"] = outcome.Note
            });
        }

        return Root(new JsonObject { ["upgrades"] = items });
    }

    public static string ForAudit(IReadOnlyList<AuditEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var items = new JsonArray();
        foreach (var entry in entries)
        {
            var cves = new JsonArray();
            foreach (var cve in entry.CveIds)
            {
                cves.Add(cve);
            }

            items.Add(new JsonObject
            {
                ["version"] = entry.Installation.Version.Raw,
                ["channel"] = entry.Channel,
                ["isManaged"] = entry.Installation.IsManaged,
                ["hostRoot"] = entry.Installation.HostRoot,
                ["metadataKnown"] = entry.MetadataKnown,
                ["vulnerable"] = entry.IsVulnerable,
                ["fixedIn"] = entry.FixedInSdk,
                ["cveIds"] = cves
            });
        }

        return Root(new JsonObject { ["sdks"] = items });
    }

    private static string Root(JsonObject payload)
    {
        var root = new JsonObject { ["schemaVersion"] = SchemaVersion };
        foreach (var pair in payload)
        {
            root[pair.Key] = pair.Value?.DeepClone();
        }

        return root.ToJsonString(Indented);
    }
}
