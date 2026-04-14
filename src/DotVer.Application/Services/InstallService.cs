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
            return new InstallPlan(token, exactVersion!.Raw, "Version exacta solicitada por el usuario.", null);
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

        return new InstallPlan(token, selectedChannel.LatestSdkVersion, $"Channel {selectedChannel.Channel} resolved to {selectedChannel.LatestSdkVersion}.", selectedChannel);
    }

    public async Task<InstallOutcome> InstallAsync(string versionOrChannel, CancellationToken cancellationToken)
    {
        var plan = await ResolveInstallPlanAsync(versionOrChannel, cancellationToken).ConfigureAwait(false);
        var alreadyInstalled = (await _inventoryService.ScanAsync(cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(sdk => sdk.IsManaged && string.Equals(sdk.Version.Raw, plan.ResolvedVersion, StringComparison.OrdinalIgnoreCase));

        if (alreadyInstalled is not null)
        {
            return new InstallOutcome(plan, alreadyInstalled, true);
        }

        Directory.CreateDirectory(_paths.ManagedInstalls);
        Directory.CreateDirectory(_paths.Scripts);

        var installRoot = Path.Combine(_paths.ManagedInstalls, plan.ResolvedVersion);
        Directory.CreateDirectory(installRoot);

        if (_environment.IsWindows)
        {
            var scriptPath = Path.Combine(_paths.Scripts, "dotnet-install.ps1");
            await _httpResourceClient.DownloadFileAsync(PowerShellInstallScript, scriptPath, cancellationToken).ConfigureAwait(false);

            var result = await _processRunner.RunAsync(
                new ProcessSpec(
                    "pwsh",
                    ["-NoProfile", "-File", scriptPath, "-Version", plan.ResolvedVersion, "-InstallDir", installRoot, "-NoPath"]),
                cancellationToken).ConfigureAwait(false);

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

            var result = await _processRunner.RunAsync(
                new ProcessSpec(
                    "/bin/bash",
                    [scriptPath, "--version", plan.ResolvedVersion, "--install-dir", installRoot, "--no-path"]),
                cancellationToken).ConfigureAwait(false);

            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(result.StandardError);
            }
        }

        var installed = (await _inventoryService.ScanAsync(cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(sdk => sdk.IsManaged && string.Equals(sdk.Version.Raw, plan.ResolvedVersion, StringComparison.OrdinalIgnoreCase));

        return new InstallOutcome(
            plan,
            installed ?? throw new InvalidOperationException($"Could not validate the installation of {plan.ResolvedVersion}."),
            false);
    }

    public async Task<IReadOnlyList<ReleaseChannel>> ListRemoteAsync(CancellationToken cancellationToken)
    {
        var payload = await _httpResourceClient.GetStringAsync(ReleasesIndexUrl, cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(payload);

        return document.RootElement
            .GetProperty("releases-index")
            .EnumerateArray()
            .Select(node => new ReleaseChannel(
                node.GetProperty("channel-version").GetString() ?? "unknown",
                node.GetProperty("latest-sdk").GetString() ?? "unknown",
                node.TryGetProperty("support-phase", out var supportPhase) ? supportPhase.GetString() ?? "unknown" : "unknown",
                node.TryGetProperty("release-type", out var releaseType) ? releaseType.GetString() ?? "unknown" : "unknown"))
            .OrderByDescending(static channel => channel.Channel, StringComparer.OrdinalIgnoreCase)
            .ToList();
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
