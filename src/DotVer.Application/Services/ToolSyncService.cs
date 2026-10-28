using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DotVer.Domain;

namespace DotVer.Application;

/// <summary>Persists ~/.dotver/tools.json: the global tools to (re)install after every SDK install.</summary>
public sealed class ToolsManifestStore : IToolsManifestStore
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private readonly DotVerPaths _paths;

    public ToolsManifestStore(DotVerPaths paths)
    {
        _paths = paths;
    }

    public string FilePath => Path.Combine(_paths.Home, "tools.json");

    public Task<ToolsManifest> LoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(FilePath))
        {
            return Task.FromResult(ToolsManifest.Empty);
        }

        JsonObject? root;
        try
        {
            root = JsonNode.Parse(File.ReadAllText(FilePath)) as JsonObject;
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"'{TerminalText.Sanitize(FilePath)}' is not valid JSON. Fix or delete it. {exception.Message}", exception);
        }

        var tools = new List<ToolEntry>();
        if (root?["tools"] is JsonArray entries)
        {
            foreach (var entry in entries)
            {
                if (entry is not JsonObject tool ||
                    tool["id"] is not JsonValue idValue || !idValue.TryGetValue<string>(out var id) ||
                    !ToolSyncService.IsValidToolId(id))
                {
                    continue;
                }

                string? version = null;
                if (tool["version"] is JsonValue versionValue && versionValue.TryGetValue<string>(out var rawVersion) &&
                    ToolSyncService.IsValidToolVersion(rawVersion))
                {
                    version = rawVersion;
                }

                tools.Add(new ToolEntry(id, version));
            }
        }

        return Task.FromResult(new ToolsManifest(tools));
    }

    public Task SaveAsync(ToolsManifest manifest, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        cancellationToken.ThrowIfCancellationRequested();

        var entries = new JsonArray();
        foreach (var tool in manifest.Tools)
        {
            var node = new JsonObject { ["id"] = tool.Id };
            if (tool.Version is not null)
            {
                node["version"] = tool.Version;
            }

            entries.Add(node);
        }

        Directory.CreateDirectory(_paths.Home);
        GlobalJsonService.WriteAtomically(FilePath, new JsonObject { ["tools"] = entries }.ToJsonString(WriteOptions) + Environment.NewLine);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Keeps global dotnet tools available across SDK switches, like nvm's
/// default-packages: `dotver tools add` records them and every
/// `dotver install` re-runs `dotnet tool update --global` with the new host.
/// </summary>
public sealed partial class ToolSyncService : IToolSyncService
{
    private const int MaxCapturedChars = 16384;

    private readonly IToolsManifestStore _store;
    private readonly IProcessRunner _processRunner;
    private readonly IEnvironmentContext _environment;

    public ToolSyncService(IToolsManifestStore store, IProcessRunner processRunner, IEnvironmentContext environment)
    {
        _store = store;
        _processRunner = processRunner;
        _environment = environment;
    }

    public Task<ToolsManifest> ListAsync(CancellationToken cancellationToken) => _store.LoadAsync(cancellationToken);

    public async Task<ToolsManifest> AddAsync(string id, string? version, CancellationToken cancellationToken)
    {
        var toolId = id?.Trim() ?? string.Empty;
        if (!IsValidToolId(toolId))
        {
            throw new InvalidOperationException($"'{TerminalText.Sanitize(toolId)}' is not a valid tool package id (letters, digits, '.', '_' and '-' only).");
        }

        var toolVersion = string.IsNullOrWhiteSpace(version) ? null : version.Trim();
        if (toolVersion is not null && !IsValidToolVersion(toolVersion))
        {
            throw new InvalidOperationException($"'{TerminalText.Sanitize(toolVersion)}' is not a valid tool version.");
        }

        var manifest = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
        var tools = manifest.Tools
            .Where(tool => !string.Equals(tool.Id, toolId, StringComparison.OrdinalIgnoreCase))
            .Append(new ToolEntry(toolId, toolVersion))
            .ToList();
        var updated = new ToolsManifest(tools);
        await _store.SaveAsync(updated, cancellationToken).ConfigureAwait(false);
        return updated;
    }

    public async Task<bool> RemoveAsync(string id, CancellationToken cancellationToken)
    {
        var manifest = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
        var remaining = manifest.Tools
            .Where(tool => !string.Equals(tool.Id, id?.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (remaining.Count == manifest.Tools.Count)
        {
            return false;
        }

        await _store.SaveAsync(new ToolsManifest(remaining), cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<ToolSyncResult> SyncAsync(SdkInstallation sdk, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sdk);
        var manifest = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
        var results = new List<ToolSyncEntry>();

        // The child runs with the selected host so the tool is restored
        // against that SDK's runtime; DOTNET_ROOT keeps its own resolver honest.
        var environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["DOTNET_ROOT"] = sdk.HostRoot,
            ["DOTVER_VERSION"] = sdk.Version.Raw
        };

        foreach (var tool in manifest.Tools)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await RunToolCommandAsync(sdk, "update", tool, environment, cancellationToken).ConfigureAwait(false);
            if (result.ExitCode != 0)
            {
                // SDKs before 8.0 refuse to update a tool that is not installed yet.
                result = await RunToolCommandAsync(sdk, "install", tool, environment, cancellationToken).ConfigureAwait(false);
            }

            var error = result.ExitCode == 0 ? null : Tail(result.StandardError.Length > 0 ? result.StandardError : result.StandardOutput);
            results.Add(new ToolSyncEntry(tool.Id, tool.Version, result.ExitCode == 0, result.ExitCode, error));
        }

        return new ToolSyncResult(sdk, results);
    }

    private async Task<ProcessResult> RunToolCommandAsync(
        SdkInstallation sdk,
        string verb,
        ToolEntry tool,
        IReadOnlyDictionary<string, string?> environment,
        CancellationToken cancellationToken)
    {
        var args = new List<string> { "tool", verb, "--global", tool.Id };
        if (tool.Version is not null)
        {
            args.AddRange(["--version", tool.Version]);
        }

        var spec = new ProcessSpec(
            sdk.DotnetPath,
            args,
            WorkingDirectory: _environment.HomeDirectory,
            EnvironmentVariables: environment,
            MaxCapturedCharsPerStream: MaxCapturedChars);

        try
        {
            return await _processRunner.RunAsync(spec, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            return new ProcessResult(127, string.Empty, exception.Message);
        }
    }

    private static string Tail(string text)
    {
        var trimmed = TerminalText.Sanitize(text.Trim());
        return trimmed.Length <= 400 ? trimmed : trimmed[^400..];
    }

    // NuGet package id charset; the values travel as single process arguments
    // (no shell) but staying inert keeps logs and JSON reports predictable.
    public static bool IsValidToolId(string? id) => id is not null && ToolIdPattern().IsMatch(id);

    public static bool IsValidToolVersion(string? version) => version is not null && ToolVersionPattern().IsMatch(version);

    [GeneratedRegex(@"\A[A-Za-z0-9][A-Za-z0-9._-]{0,99}\z", RegexOptions.CultureInvariant)]
    private static partial Regex ToolIdPattern();

    [GeneratedRegex(@"\A[0-9A-Za-z][0-9A-Za-z.+-]{0,63}\z", RegexOptions.CultureInvariant)]
    private static partial Regex ToolVersionPattern();
}
