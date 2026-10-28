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
                ["architecture"] = sdk.Architecture,
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

    public static string ForExplain(SelectionTrace trace)
    {
        ArgumentNullException.ThrowIfNull(trace);

        var steps = new JsonArray();
        foreach (var step in trace.Steps)
        {
            steps.Add(new JsonObject
            {
                ["source"] = step.Source,
                ["value"] = step.Value,
                ["outcome"] = step.Outcome,
                ["detail"] = step.Detail,
                ["winner"] = step.Winner
            });
        }

        var winner = trace.Steps.FirstOrDefault(static step => step.Winner);
        return Root(new JsonObject
        {
            ["winner"] = new JsonObject
            {
                ["source"] = winner?.Source,
                ["version"] = trace.Selection.Installation?.Version.Raw,
                ["reason"] = trace.Selection.Reason,
                ["hostRoot"] = trace.Selection.Installation?.HostRoot,
                ["dotnetPath"] = trace.Selection.Installation?.DotnetPath
            },
            ["steps"] = steps,
            ["error"] = trace.Error
        });
    }

    public static string ForIdeSettings(IdeSettingsResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var keys = new JsonArray();
        foreach (var key in result.KeysWritten)
        {
            keys.Add(key);
        }

        return Root(new JsonObject
        {
            ["filePath"] = result.FilePath,
            ["created"] = result.Created,
            ["hostRoot"] = result.HostRoot,
            ["keys"] = keys,
            ["commentsDropped"] = result.CommentsDropped
        });
    }

    public static string ForToolsManifest(ToolsManifest manifest, string filePath)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var tools = new JsonArray();
        foreach (var tool in manifest.Tools)
        {
            tools.Add(new JsonObject { ["id"] = tool.Id, ["version"] = tool.Version });
        }

        return Root(new JsonObject { ["filePath"] = filePath, ["tools"] = tools });
    }

    public static string ForToolSync(ToolSyncResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var tools = new JsonArray();
        foreach (var tool in result.Tools)
        {
            tools.Add(new JsonObject
            {
                ["id"] = tool.Id,
                ["version"] = tool.Version,
                ["succeeded"] = tool.Succeeded,
                ["exitCode"] = tool.ExitCode,
                ["error"] = tool.Error
            });
        }

        return Root(new JsonObject
        {
            ["sdkVersion"] = result.Sdk.Version.Raw,
            ["hostRoot"] = result.Sdk.HostRoot,
            ["failed"] = result.Failed,
            ["tools"] = tools
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
                ["architecture"] = entry.Architecture,
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

    public static string ForScan(
        WorkspaceScanResult scan,
        IReadOnlyList<string>? installedVersions = null,
        IReadOnlyList<string>? errors = null,
        IReadOnlyList<string>? wouldInstall = null)
    {
        ArgumentNullException.ThrowIfNull(scan);

        var items = new JsonArray();
        foreach (var entry in scan.Entries)
        {
            items.Add(new JsonObject
            {
                ["filePath"] = entry.FilePath,
                ["requestedVersion"] = entry.RequestedVersion,
                ["rollForward"] = entry.RollForward,
                ["matchedVersion"] = entry.MatchedVersion,
                ["satisfied"] = entry.Satisfied,
                ["invalid"] = entry.Invalid
            });
        }

        var payload = new JsonObject
        {
            ["root"] = scan.Root,
            ["truncated"] = scan.Truncated,
            ["pins"] = items
        };

        if (installedVersions is not null)
        {
            var installed = new JsonArray();
            foreach (var version in installedVersions)
            {
                installed.Add(version);
            }

            payload["installedVersions"] = installed;
        }

        if (errors is not null)
        {
            var errorItems = new JsonArray();
            foreach (var error in errors)
            {
                errorItems.Add(error);
            }

            payload["errors"] = errorItems;
        }

        if (wouldInstall is not null)
        {
            var planned = new JsonArray();
            foreach (var version in wouldInstall)
            {
                planned.Add(version);
            }

            payload["wouldInstall"] = planned;
        }

        return Root(payload);
    }

    public static string ForMatrix(MatrixResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var items = new JsonArray();
        foreach (var entry in result.Entries)
        {
            items.Add(new JsonObject
            {
                ["token"] = entry.Token,
                ["version"] = entry.Version,
                ["exitCode"] = entry.ExitCode,
                ["durationMs"] = entry.DurationMs,
                ["error"] = entry.Error,
                ["stdout"] = entry.StandardOutput,
                ["stderr"] = entry.StandardError
            });
        }

        return Root(new JsonObject
        {
            ["allSucceeded"] = result.AllSucceeded,
            ["entries"] = items
        });
    }

    public static string ForInfo(SdkInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);

        var runtimes = new JsonArray();
        foreach (var runtime in info.Runtimes)
        {
            var versions = new JsonArray();
            foreach (var version in runtime.Versions)
            {
                versions.Add(version);
            }

            runtimes.Add(new JsonObject
            {
                ["kind"] = runtime.Kind,
                ["versions"] = versions
            });
        }

        var workloads = new JsonArray();
        foreach (var workload in info.Workloads)
        {
            workloads.Add(workload);
        }

        return Root(new JsonObject
        {
            ["version"] = info.Installation.Version.Raw,
            ["architecture"] = info.Installation.Architecture,
            ["hostRoot"] = info.Installation.HostRoot,
            ["dotnetPath"] = info.Installation.DotnetPath,
            ["isManaged"] = info.Installation.IsManaged,
            ["runtimes"] = runtimes,
            ["workloads"] = workloads
        });
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
