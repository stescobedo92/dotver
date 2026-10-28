using DotVer.Domain;

namespace DotVer.Application;

public sealed class SelectionService : ISelectionService
{
    private readonly IInventoryService _inventoryService;
    private readonly IConfigStore _configStore;
    private readonly IGlobalJsonService _globalJsonService;
    private readonly IVersionResolver _versionResolver;
    private readonly IEnvironmentContext _environment;

    public SelectionService(
        IInventoryService inventoryService,
        IConfigStore configStore,
        IGlobalJsonService globalJsonService,
        IVersionResolver versionResolver,
        IEnvironmentContext environment)
    {
        _inventoryService = inventoryService;
        _configStore = configStore;
        _globalJsonService = globalJsonService;
        _versionResolver = versionResolver;
        _environment = environment;
    }

    public async Task<SdkSelection> ResolveAsync(CancellationToken cancellationToken)
    {
        var trace = await ExplainAsync(cancellationToken).ConfigureAwait(false);
        return trace.Error is null
            ? trace.Selection
            : throw new InvalidOperationException(trace.Error);
    }

    /// <summary>
    /// Evaluates the whole chain (DOTVER_VERSION > global.json > current > default > system)
    /// and records why every step won, lost or was skipped. ResolveAsync is this
    /// trace's selection, so the explanation can never disagree with the shim.
    /// </summary>
    public async Task<SelectionTrace> ExplainAsync(CancellationToken cancellationToken)
    {
        var installations = await _inventoryService.ScanAsync(cancellationToken).ConfigureAwait(false);
        var config = await _configStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        var hostArchitecture = _environment.RuntimeArchitecture;
        var steps = new List<SelectionStep>();
        SdkSelection? winner = null;
        string? error = null;

        // 1. Explicit per-process override (set by `dotver exec`). Only exact
        //    installed versions are honored: no aliases/prefixes, and an
        //    unresolvable value fails loudly instead of silently falling through.
        var overrideToken = _environment.GetEnvironmentVariable("DOTVER_VERSION");
        if (string.IsNullOrWhiteSpace(overrideToken))
        {
            steps.Add(new SelectionStep("DOTVER_VERSION", null, SelectionOutcomes.Skipped, "Not set (dotver exec sets it for its child process).", Winner: false));
        }
        else
        {
            var token = Sanitize(overrideToken);
            var overridden = SdkVersion.TryParse(token, out _)
                ? installations
                    .Where(sdk => string.Equals(sdk.Version.Raw, token, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(sdk => Architectures.MatchesHost(sdk.Architecture, hostArchitecture) ? 0 : 1)
                    .FirstOrDefault()
                : null;

            if (overridden is not null)
            {
                winner = new SdkSelection(overridden, $"Selected by DOTVER_VERSION ({overridden.Version.Raw})", false);
                steps.Add(new SelectionStep("DOTVER_VERSION", token, SelectionOutcomes.Selected, $"Exact installed version at {overridden.HostRoot}.", Winner: true));
            }
            else
            {
                error = $"DOTVER_VERSION is set to '{token}' but that exact SDK version is not installed. Unset DOTVER_VERSION or install it first.";
                steps.Add(new SelectionStep(
                    "DOTVER_VERSION",
                    token,
                    SelectionOutcomes.Error,
                    "No installed SDK has exactly this version, so every dotnet call fails until it is unset or installed. The steps below show what would win afterwards.",
                    Winner: false));
            }
        }

        // 2. A project pin wins inside its directory tree.
        steps.Add(EvaluateGlobalJson(installations, hostArchitecture, ref winner));

        // 3./4. Persisted selections are automatic: a foreign-arch install left
        //       behind after the native one was removed must not be adopted.
        steps.Add(EvaluatePersisted("current", config.CurrentVersion, "dotver use", "persisted 'use'", config, installations, hostArchitecture, ref winner));
        steps.Add(EvaluatePersisted("default", config.DefaultVersion, "dotver default", "default", config, installations, hostArchitecture, ref winner));

        // 5. The system host, if any.
        var systemPath = _inventoryService.GetSystemDotnetPath();
        var comparison = _environment.IsWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var systemInstallation = installations.FirstOrDefault(sdk => string.Equals(sdk.DotnetPath, systemPath, comparison));
        var systemSelection = new SdkSelection(systemInstallation, "Selected by system fallback", false);
        if (systemPath is null)
        {
            steps.Add(new SelectionStep(
                "system",
                null,
                SelectionOutcomes.Unmatched,
                winner is null
                    ? "No dotnet host in the known system roots: the shim fails until an SDK is installed or selected."
                    : "No dotnet host in the known system roots.",
                Winner: false));
        }
        else
        {
            var detail = systemInstallation is null
                ? "dotnet host found; the host itself picks the SDK (no dotver inventory entry for it)."
                : $"dotnet host found with SDK {systemInstallation.Version.Raw}.";
            steps.Add(Claim(ref winner, systemSelection, "system", systemPath, detail));
        }

        return new SelectionTrace(steps, winner ?? systemSelection, error);
    }

    private SelectionStep EvaluateGlobalJson(IReadOnlyList<SdkInstallation> installations, string hostArchitecture, ref SdkSelection? winner)
    {
        var path = _globalJsonService.FindNearest(_environment.CurrentDirectory);
        if (path is null)
        {
            return new SelectionStep("global.json", null, SelectionOutcomes.Skipped, $"No global.json at or above {_environment.CurrentDirectory}.", Winner: false);
        }

        var read = _globalJsonService.Read(path);
        if (read.Status == GlobalJsonReadStatus.Invalid)
        {
            return new SelectionStep("global.json", path, SelectionOutcomes.Skipped, "The file is invalid, linked, oversized or unreadable and is ignored.", Winner: false);
        }

        if (read.Request is null)
        {
            return new SelectionStep("global.json", path, SelectionOutcomes.Skipped, "The file has no sdk.version pin.", Winner: false);
        }

        var request = read.Request;
        var policy = request.RollForward == SdkRollForward.Default
            ? "disable (default)"
            : GlobalJsonService.FormatRollForward(request.RollForward);
        var pinned = GlobalJsonMatcher.Match(request, installations, hostArchitecture);
        if (pinned is not null)
        {
            var candidate = new SdkSelection(pinned, $"Selected by global.json ({request.Version.Raw})", true);
            return Claim(ref winner, candidate, "global.json", path, $"Pin {request.Version.Raw} (rollForward: {policy}) matched installed {pinned.Version.Raw}.");
        }

        var installed = installations
            .Where(sdk => Architectures.MatchesHost(sdk.Architecture, hostArchitecture))
            .Select(static sdk => sdk.Version.Raw)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(static raw => SdkVersion.Parse(raw))
            .ToList();
        return new SelectionStep(
            "global.json",
            path,
            SelectionOutcomes.Unmatched,
            $"No installed SDK satisfies pin {request.Version.Raw} with rollForward {policy}. Installed for this architecture: {(installed.Count == 0 ? "none" : string.Join(", ", installed))}. Run 'dotver ensure' to install it.",
            Winner: false);
    }

    private SelectionStep EvaluatePersisted(
        string source,
        string? token,
        string setBy,
        string reasonLabel,
        DotVerConfig config,
        IReadOnlyList<SdkInstallation> installations,
        string hostArchitecture,
        ref SdkSelection? winner)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return new SelectionStep(source, null, SelectionOutcomes.Skipped, $"Not set ({setBy}).", Winner: false);
        }

        var safeToken = Sanitize(token);
        var resolved = _versionResolver.FindByToken(token, config, installations);
        if (resolved is null)
        {
            return new SelectionStep(source, safeToken, SelectionOutcomes.Unmatched, $"'{safeToken}' does not resolve to an installed SDK (run 'dotver doctor').", Winner: false);
        }

        if (!Architectures.MatchesHost(resolved.Architecture, hostArchitecture))
        {
            return new SelectionStep(
                source,
                safeToken,
                SelectionOutcomes.Unmatched,
                $"{resolved.Version.Raw} is only installed for {resolved.Architecture}; automatic selection never uses a foreign architecture.",
                Winner: false);
        }

        var candidate = new SdkSelection(resolved, $"Selected by {reasonLabel} ({token})", false);
        return Claim(ref winner, candidate, source, safeToken, $"Resolved to installed {resolved.Version.Raw} at {resolved.HostRoot}.");
    }

    private static SelectionStep Claim(ref SdkSelection? winner, SdkSelection candidate, string source, string? value, string detail)
    {
        if (winner is null)
        {
            winner = candidate;
            return new SelectionStep(source, value, SelectionOutcomes.Selected, detail, Winner: true);
        }

        return new SelectionStep(source, value, SelectionOutcomes.Overridden, detail + " A higher-priority source already won.", Winner: false);
    }

    private static string Sanitize(string value) =>
        new(value.Trim().Where(static c => !char.IsControl(c)).ToArray());
}
