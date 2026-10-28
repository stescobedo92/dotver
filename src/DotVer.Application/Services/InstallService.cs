using System.Text.Json;
using DotVer.Domain;

namespace DotVer.Application;

public sealed class InstallService : IInstallService
{
    private const string PowerShellInstallScript = "https://dot.net/v1/dotnet-install.ps1";
    private const string ShellInstallScript = "https://dot.net/v1/dotnet-install.sh";
    private const string ReleasesIndexUrl = "https://builds.dotnet.microsoft.com/dotnet/release-metadata/releases-index.json";

    private readonly DotVerPaths _paths;
    private readonly IEnvironmentContext _environment;
    private readonly IHttpResourceClient _httpResourceClient;
    private readonly IProcessRunner _processRunner;
    private readonly IInventoryService _inventoryService;

    public InstallService(
        DotVerPaths paths,
        IEnvironmentContext environment,
        IHttpResourceClient httpResourceClient,
        IProcessRunner processRunner,
        IInventoryService inventoryService)
    {
        _paths = paths;
        _environment = environment;
        _httpResourceClient = httpResourceClient;
        _processRunner = processRunner;
        _inventoryService = inventoryService;
    }

    public async Task<InstallPlan> ResolveInstallPlanAsync(string token, CancellationToken cancellationToken)
    {
        if (SdkVersion.TryParse(token, out var exactVersion))
        {
            return new InstallPlan(token, exactVersion!.Raw, "Exact version requested by the user.", null);
        }

        var channels = await ListRemoteAsync(cancellationToken).ConfigureAwait(false);
        var normalized = token.Trim().ToLowerInvariant();

        ReleaseChannel? selectedChannel = normalized switch
        {
            "latest" => channels
                .Where(static channel => !string.Equals(channel.SupportPhase, "preview", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(ParseChannelVersion)
                .FirstOrDefault(),
            "lts" => channels
                .Where(channel => string.Equals(channel.ReleaseType, "lts", StringComparison.OrdinalIgnoreCase) &&
                                  string.Equals(channel.SupportPhase, "active", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(ParseChannelVersion)
                .FirstOrDefault(),
            "sts" => channels
                .Where(channel => string.Equals(channel.ReleaseType, "sts", StringComparison.OrdinalIgnoreCase) &&
                                  string.Equals(channel.SupportPhase, "active", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(ParseChannelVersion)
                .FirstOrDefault(),
            _ => channels.FirstOrDefault(channel =>
                string.Equals(channel.Channel, normalized, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(channel.Channel, NormalizeChannelToken(normalized), StringComparison.OrdinalIgnoreCase))
        };

        if (selectedChannel is null)
        {
            throw new InvalidOperationException($"Could not resolve '{token}' as a version or channel.");
        }

        // Remote metadata is untrusted input for path construction and for
        // installer arguments: fail closed on BOTH the version and the channel.
        if (!IsValidChannelName(selectedChannel.Channel))
        {
            throw new InvalidOperationException(
                $"Release metadata returned an invalid channel name '{TerminalText.Sanitize(selectedChannel.Channel)}'.");
        }

        if (!SdkVersion.TryParse(selectedChannel.LatestSdkVersion, out _))
        {
            throw new InvalidOperationException(
                $"Release metadata for channel {selectedChannel.Channel} returned an invalid SDK version '{TerminalText.Sanitize(selectedChannel.LatestSdkVersion)}'.");
        }

        return new InstallPlan(token, selectedChannel.LatestSdkVersion, $"Channel {selectedChannel.Channel} resolved to {selectedChannel.LatestSdkVersion}.", selectedChannel);
    }

    public async Task<InstallOutcome> InstallAsync(string versionOrChannel, InstallOptions? options, CancellationToken cancellationToken)
    {
        var architecture = Architectures.Normalize(options?.Architecture, _environment.RuntimeArchitecture);
        var explicitArchitecture = options?.Architecture is null ? null : architecture;
        var plan = await ResolveInstallPlanAsync(versionOrChannel, cancellationToken).ConfigureAwait(false);

        var alreadyInstalled = (await _inventoryService.ScanAsync(cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(sdk => sdk.IsManaged &&
                                   string.Equals(sdk.Version.Raw, plan.ResolvedVersion, StringComparison.OrdinalIgnoreCase) &&
                                   Architectures.MatchesHost(sdk.Architecture ?? _environment.RuntimeArchitecture, architecture));

        if (alreadyInstalled is not null)
        {
            return new InstallOutcome(plan, alreadyInstalled, true);
        }

        if (!SdkVersion.TryParse(plan.ResolvedVersion, out _))
        {
            throw new InvalidOperationException($"Refusing to install invalid SDK version '{plan.ResolvedVersion}'.");
        }

        Directory.CreateDirectory(_paths.ManagedInstalls);
        var installRoot = ResolveManagedInstallRoot(ManagedLeaf(plan.ResolvedVersion, architecture));
        await RunInstallScriptAsync(
            version: plan.ResolvedVersion,
            channel: null,
            runtime: null,
            explicitArchitecture,
            installRoot,
            cancellationToken).ConfigureAwait(false);

        var installed = (await _inventoryService.ScanAsync(cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(sdk => sdk.IsManaged &&
                                   string.Equals(sdk.Version.Raw, plan.ResolvedVersion, StringComparison.OrdinalIgnoreCase) &&
                                   Architectures.MatchesHost(sdk.Architecture ?? _environment.RuntimeArchitecture, architecture));

        return new InstallOutcome(
            plan,
            installed ?? throw new InvalidOperationException($"Could not validate the installation of {plan.ResolvedVersion} ({architecture})."),
            false);
    }

    public async Task<RuntimeInstallOutcome> InstallRuntimeAsync(
        string versionOrChannel,
        string runtime,
        string? architecture,
        CancellationToken cancellationToken)
    {
        var normalizedRuntime = NormalizeRuntime(runtime);
        var arch = Architectures.Normalize(architecture, _environment.RuntimeArchitecture);
        var explicitArchitecture = architecture is null ? null : arch;

        string? version = null;
        string? channel = null;
        string label;
        if (SdkVersion.TryParse(versionOrChannel.Trim(), out var exact))
        {
            version = exact!.Raw;
            label = version;
        }
        else
        {
            var plan = await ResolveInstallPlanAsync(versionOrChannel, cancellationToken).ConfigureAwait(false);
            channel = plan.Channel?.Channel
                ?? throw new InvalidOperationException($"Could not resolve '{TerminalText.Sanitize(versionOrChannel)}' as a runtime version or channel.");
            label = channel;
        }

        var leaf = $"{normalizedRuntime}-{label}" +
                   (Architectures.MatchesHost(arch, _environment.RuntimeArchitecture) ? string.Empty : $"--{arch}");
        var installRoot = ResolveContainedRoot(_paths.ManagedRuntimes, leaf);
        var frameworkDirectory = Path.Combine(installRoot, "shared", FrameworkDirectory(normalizedRuntime));

        var existing = ListVersionDirectories(frameworkDirectory);
        if (version is not null &&
            existing.Contains(version, StringComparer.OrdinalIgnoreCase) &&
            HasRuntimePayload(frameworkDirectory, version, normalizedRuntime))
        {
            return new RuntimeInstallOutcome(normalizedRuntime, label, installRoot, existing, WasAlreadyInstalled: true);
        }

        await RunInstallScriptAsync(version, channel, normalizedRuntime, explicitArchitecture, installRoot, cancellationToken)
            .ConfigureAwait(false);

        var installedVersions = ListVersionDirectories(frameworkDirectory);
        if (installedVersions.Count == 0 ||
            (version is not null &&
             (!installedVersions.Contains(version, StringComparer.OrdinalIgnoreCase) ||
              !HasRuntimePayload(frameworkDirectory, version, normalizedRuntime))) ||
            (version is null && !installedVersions.Any(v => HasRuntimePayload(frameworkDirectory, v, normalizedRuntime))))
        {
            throw new InvalidOperationException($"Could not validate the {normalizedRuntime} runtime installation in '{TerminalText.Sanitize(installRoot)}'.");
        }

        return new RuntimeInstallOutcome(normalizedRuntime, label, installRoot, installedVersions, WasAlreadyInstalled: false);
    }

    private async Task RunInstallScriptAsync(
        string? version,
        string? channel,
        string? runtime,
        string? architecture,
        string installRoot,
        CancellationToken cancellationToken)
    {
        // Never extract through a pre-planted link/junction in the target leaf.
        if (Directory.Exists(installRoot) && (File.GetAttributes(installRoot) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException($"Refusing to install into a symlinked directory: '{TerminalText.Sanitize(installRoot)}'.");
        }

        Directory.CreateDirectory(_paths.Scripts);
        Directory.CreateDirectory(installRoot);

        if (_environment.IsWindows)
        {
            var scriptPath = Path.Combine(_paths.Scripts, "dotnet-install.ps1");
            await _httpResourceClient.DownloadFileAsync(PowerShellInstallScript, scriptPath, cancellationToken).ConfigureAwait(false);

            // Never resolve pwsh through the current directory (may be an untrusted repo).
            var powerShell = ExecutableLocator.FindOnPath(_environment, "pwsh.exe")
                ?? throw new InvalidOperationException("Could not find pwsh.exe on PATH (the current directory is intentionally excluded).");

            // TLS authenticates the CDN; Authenticode authenticates the publisher.
            await VerifyAuthenticodeSignatureAsync(powerShell, scriptPath, cancellationToken).ConfigureAwait(false);

            var args = new List<string> { "-NoProfile", "-File", scriptPath, "-InstallDir", installRoot, "-NoPath" };
            if (version is not null)
            {
                args.AddRange(["-Version", version]);
            }
            else
            {
                args.AddRange(["-Channel", channel!]);
            }

            if (runtime is not null)
            {
                args.AddRange(["-Runtime", runtime]);
            }

            if (architecture is not null)
            {
                args.AddRange(["-Architecture", architecture]);
            }

            var result = await _processRunner.RunAsync(new ProcessSpec(powerShell, args), cancellationToken).ConfigureAwait(false);
            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(result.StandardError);
            }
        }
        else
        {
            var scriptPath = Path.Combine(_paths.Scripts, "dotnet-install.sh");
            await _httpResourceClient.DownloadFileAsync(ShellInstallScript, scriptPath, cancellationToken).ConfigureAwait(false);
            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                File.SetUnixFileMode(scriptPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            var args = new List<string> { scriptPath, "--install-dir", installRoot, "--no-path" };
            if (version is not null)
            {
                args.AddRange(["--version", version]);
            }
            else
            {
                args.AddRange(["--channel", channel!]);
            }

            if (runtime is not null)
            {
                args.AddRange(["--runtime", runtime]);
            }

            if (architecture is not null)
            {
                args.AddRange(["--architecture", architecture]);
            }

            var result = await _processRunner.RunAsync(new ProcessSpec("/bin/bash", args), cancellationToken).ConfigureAwait(false);
            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(result.StandardError);
            }
        }
    }

    // Runs under -Command with the script path supplied through the environment,
    // so no quoting rule of PowerShell can be confused by the path itself.
    private const string AuthenticodeCheck =
        "$ErrorActionPreference = 'Stop'; " +
        "$signature = Get-AuthenticodeSignature -LiteralPath $env:DOTVER_SCRIPT; " +
        "if ($signature.Status -ne 'Valid') { Write-Error ('Signature status: ' + $signature.Status); exit 1 }; " +
        "if ($signature.SignerCertificate.Subject -notmatch '(^|, )CN=Microsoft Corporation(,|$)') { Write-Error ('Unexpected signer: ' + $signature.SignerCertificate.Subject); exit 2 }; " +
        "exit 0";

    private async Task VerifyAuthenticodeSignatureAsync(string powerShell, string scriptPath, CancellationToken cancellationToken)
    {
        var spec = new ProcessSpec(
            powerShell,
            ["-NoProfile", "-NonInteractive", "-Command", AuthenticodeCheck],
            EnvironmentVariables: new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase) { ["DOTVER_SCRIPT"] = scriptPath });

        var result = await _processRunner.RunAsync(spec, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode == 0)
        {
            return;
        }

        // Do not leave an unverified script lying around in the cache.
        try
        {
            File.Delete(scriptPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Best effort cleanup; the refusal below is what matters.
        }

        throw new InvalidOperationException(
            "Refusing to run dotnet-install.ps1: its Authenticode signature is missing, invalid or not issued to Microsoft Corporation. " +
            TerminalText.Sanitize(result.StandardError.Trim()));
    }

    private string ManagedLeaf(string version, string architecture) =>
        Architectures.MatchesHost(architecture, _environment.RuntimeArchitecture)
            ? version
            : $"{version}--{architecture}";

    private string NormalizeRuntime(string runtime)
    {
        var normalized = runtime.Trim().ToLowerInvariant();
        if (normalized is not ("dotnet" or "aspnetcore" or "windowsdesktop"))
        {
            throw new InvalidOperationException($"Unknown runtime '{TerminalText.Sanitize(runtime.Trim())}'. Supported: dotnet, aspnetcore, windowsdesktop.");
        }

        if (normalized == "windowsdesktop" && !_environment.IsWindows)
        {
            throw new InvalidOperationException("The windowsdesktop runtime is only available on Windows.");
        }

        return normalized;
    }

    private static string FrameworkDirectory(string runtime) => runtime switch
    {
        "aspnetcore" => "Microsoft.AspNetCore.App",
        "windowsdesktop" => "Microsoft.WindowsDesktop.App",
        _ => "Microsoft.NETCore.App"
    };

    private static IReadOnlyList<string> ListVersionDirectories(string directory) =>
        Directory.Exists(directory)
            ? Directory.EnumerateDirectories(directory)
                .Select(Path.GetFileName)
                .Where(static name => name is not null && SdkVersion.TryParse(name, out _))
                .Select(static name => name!)
                .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
                .ToList()
            : [];

    private static bool HasRuntimePayload(string frameworkDirectory, string version, string runtime)
    {
        var versionDirectory = Path.Combine(frameworkDirectory, version);
        if (!Directory.Exists(versionDirectory))
        {
            return false;
        }

        var frameworkName = FrameworkDirectory(runtime);
        return File.Exists(Path.Combine(versionDirectory, frameworkName + ".dll")) ||
               File.Exists(Path.Combine(versionDirectory, frameworkName + ".deps.json"));
    }

    public async Task<IReadOnlyList<ReleaseChannel>> ListRemoteAsync(CancellationToken cancellationToken)
    {
        var payload = await _httpResourceClient.GetStringAsync(ReleasesIndexUrl, cancellationToken).ConfigureAwait(false);

        // Remote metadata is untrusted input: a malformed document must fail
        // with one clear error, and a malformed entry must be skipped.
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(payload);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("Release metadata is not valid JSON.", exception);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("releases-index", out var index) ||
                index.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException("Release metadata is malformed: expected an object with a releases-index array.");
            }

            var channels = new List<ReleaseChannel>();
            foreach (var node in index.EnumerateArray())
            {
                if (node.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var channel = ReadString(node, "channel-version");
                var latestSdk = ReadString(node, "latest-sdk");
                if (channel is null || latestSdk is null || !IsValidChannelName(channel))
                {
                    continue;
                }

                channels.Add(new ReleaseChannel(
                    channel,
                    TerminalText.Sanitize(latestSdk),
                    TerminalText.Sanitize(ReadString(node, "support-phase") ?? "unknown"),
                    TerminalText.Sanitize(ReadString(node, "release-type") ?? "unknown"),
                    ReadString(node, "releases.json")));
            }

            return channels
                .OrderByDescending(static channel => channel.Channel, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    private static string? ReadString(JsonElement node, string name) =>
        node.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private string ResolveManagedInstallRoot(string leaf) => ResolveContainedRoot(_paths.ManagedInstalls, leaf);

    private static bool IsValidChannelName(string? channel) =>
        channel is not null &&
        channel.Length is >= 3 and <= 16 &&
        channel.Count(c => c == '.') == 1 &&
        channel.All(static c => (c >= '0' && c <= '9') || c == '.') &&
        !channel.StartsWith('.') && !channel.EndsWith('.');

    private string ResolveContainedRoot(string baseDirectory, string leaf)
    {
        // The leaf must be a single directory name: no separators, no traversal.
        if (string.IsNullOrWhiteSpace(leaf) || !string.Equals(Path.GetFileName(leaf), leaf, StringComparison.Ordinal) ||
            leaf.Contains(Path.DirectorySeparatorChar) || leaf.Contains(Path.AltDirectorySeparatorChar))
        {
            throw new InvalidOperationException($"Refusing to use '{leaf}' as a managed install directory.");
        }

        var managedRoot = Path.GetFullPath(baseDirectory);
        var candidate = Path.GetFullPath(Path.Combine(managedRoot, leaf));
        var comparison = _environment.IsWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!candidate.StartsWith(managedRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, comparison))
        {
            throw new InvalidOperationException($"Refusing to install outside the managed directory: '{TerminalText.Sanitize(candidate)}'.");
        }

        return candidate;
    }

    private static Version ParseChannelVersion(ReleaseChannel channel)
    {
        var value = channel.Channel.Contains('.', StringComparison.Ordinal) ? channel.Channel : channel.Channel + ".0";
        return Version.TryParse(value, out var version) ? version : new Version(0, 0);
    }

    private static string NormalizeChannelToken(string token) =>
        token.EndsWith(".0", StringComparison.OrdinalIgnoreCase)
            ? token
            : token.Contains('.', StringComparison.Ordinal) ? token : token + ".0";
}
