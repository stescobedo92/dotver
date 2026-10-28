using DotVer.Domain;

namespace DotVer.Application;

public interface IDownloadSources
{
    Task<DownloadSources> ResolveAsync(CancellationToken cancellationToken);
}

public interface IMirrorService
{
    Task<MirrorConfig?> ShowAsync(CancellationToken cancellationToken);

    /// <summary>Stores the mirror. A null argument keeps the official source for that item.</summary>
    Task<MirrorConfig> SetAsync(string? installScriptBaseUrl, string? releasesIndexUrl, CancellationToken cancellationToken);

    Task<bool> ClearAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Resolves download URLs from config.json. A mirror never relaxes the
/// transport rules: it must be plain https and only its own host joins the
/// allow-list, so the security posture is explicit and inspectable.
/// </summary>
public sealed class ConfigDownloadSources : IDownloadSources
{
    private readonly IConfigStore _configStore;

    public ConfigDownloadSources(IConfigStore configStore)
    {
        _configStore = configStore;
    }

    public async Task<DownloadSources> ResolveAsync(CancellationToken cancellationToken)
    {
        var config = await _configStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        return Resolve(config.Mirror);
    }

    public static DownloadSources Resolve(MirrorConfig? mirror)
    {
        if (mirror is null || (mirror.InstallScriptBaseUrl is null && mirror.ReleasesIndexUrl is null))
        {
            return DownloadSources.Official;
        }

        var hosts = new List<string>();
        var powerShell = DownloadSources.OfficialPowerShellInstallScriptUrl;
        var shell = DownloadSources.OfficialShellInstallScriptUrl;
        var releasesIndex = DownloadSources.OfficialReleasesIndexUrl;

        if (mirror.InstallScriptBaseUrl is not null)
        {
            var baseUri = MirrorService.ValidateUrl(mirror.InstallScriptBaseUrl, "install script base URL");
            var baseUrl = baseUri.AbsoluteUri.TrimEnd('/');
            powerShell = baseUrl + "/dotnet-install.ps1";
            shell = baseUrl + "/dotnet-install.sh";
            hosts.Add(baseUri.Host);
        }

        if (mirror.ReleasesIndexUrl is not null)
        {
            var indexUri = MirrorService.ValidateUrl(mirror.ReleasesIndexUrl, "releases index URL");
            releasesIndex = indexUri.AbsoluteUri;
            hosts.Add(indexUri.Host);
        }

        return new DownloadSources(
            powerShell,
            shell,
            releasesIndex,
            hosts.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            IsMirrored: true);
    }
}

public sealed class MirrorService : IMirrorService
{
    private readonly IConfigStore _configStore;

    public MirrorService(IConfigStore configStore)
    {
        _configStore = configStore;
    }

    public async Task<MirrorConfig?> ShowAsync(CancellationToken cancellationToken) =>
        (await _configStore.LoadAsync(cancellationToken).ConfigureAwait(false)).Mirror;

    public async Task<MirrorConfig> SetAsync(string? installScriptBaseUrl, string? releasesIndexUrl, CancellationToken cancellationToken)
    {
        var scripts = string.IsNullOrWhiteSpace(installScriptBaseUrl)
            ? null
            : ValidateUrl(installScriptBaseUrl, "install script base URL").AbsoluteUri.TrimEnd('/');
        var index = string.IsNullOrWhiteSpace(releasesIndexUrl)
            ? null
            : ValidateUrl(releasesIndexUrl, "releases index URL").AbsoluteUri;

        if (scripts is null && index is null)
        {
            throw new InvalidOperationException("Pass --install-script <url> and/or --releases-index <url>.");
        }

        var config = await _configStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        var mirror = new MirrorConfig(
            scripts ?? config.Mirror?.InstallScriptBaseUrl,
            index ?? config.Mirror?.ReleasesIndexUrl);
        await _configStore.SaveAsync(config with { Mirror = mirror }, cancellationToken).ConfigureAwait(false);
        return mirror;
    }

    public async Task<bool> ClearAsync(CancellationToken cancellationToken)
    {
        var config = await _configStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (config.Mirror is null)
        {
            return false;
        }

        await _configStore.SaveAsync(config with { Mirror = null }, cancellationToken).ConfigureAwait(false);
        return true;
    }

    public static Uri ValidateUrl(string value, string role)
    {
        if (Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) &&
            string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrEmpty(uri.UserInfo) &&
            string.IsNullOrEmpty(uri.Query) &&
            string.IsNullOrEmpty(uri.Fragment) &&
            !string.IsNullOrWhiteSpace(uri.Host))
        {
            return uri;
        }

        throw new InvalidOperationException(
            $"The {role} must be an absolute https URL without credentials, query or fragment; got '{TerminalText.Sanitize(value)}'.");
    }
}
