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
        IReadOnlyList<string> warnings)
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

        return Root(new JsonObject
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
