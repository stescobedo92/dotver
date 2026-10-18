using DotVer.Domain;

namespace DotVer.Application;

public sealed class VersionResolver : IVersionResolver
{
    private readonly string? _hostArchitecture;

    public VersionResolver(IEnvironmentContext? environment = null)
    {
        _hostArchitecture = environment?.RuntimeArchitecture;
    }
    public string ResolveToken(string token, DotVerConfig config, IReadOnlyList<SdkInstallation> installations)
    {
        if (string.Equals(token, "current", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(config.CurrentVersion))
        {
            return config.CurrentVersion;
        }

        if (string.Equals(token, "default", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(config.DefaultVersion))
        {
            return config.DefaultVersion;
        }

        if (string.Equals(token, "latest", StringComparison.OrdinalIgnoreCase))
        {
            // "latest" is quasi-automatic: never resolve it to a foreign-arch
            // install just because its version number is higher.
            var pool = _hostArchitecture is null
                ? installations
                : installations.Where(sdk => Architectures.MatchesHost(sdk.Architecture, _hostArchitecture)).ToList();
            return pool
                .OrderByDescending(static sdk => sdk.Version)
                .FirstOrDefault()
                ?.Version.Raw
                ?? token;
        }

        if (config.Aliases.TryGetValue(token, out var alias))
        {
            return alias;
        }

        return token;
    }

    public SdkInstallation? FindByToken(string token, DotVerConfig config, IReadOnlyList<SdkInstallation> installations)
    {
        var resolved = ResolveToken(token, config, installations);
        // Prefer the host build when the same version exists for several
        // architectures (explicit exact tokens can still reach a foreign one
        // when it is the only build of that version).
        var ordered = installations
            .OrderByDescending(static sdk => sdk.Version)
            .ThenBy(sdk => _hostArchitecture is not null && !Architectures.MatchesHost(sdk.Architecture, _hostArchitecture) ? 1 : 0);

        var exact = ordered.FirstOrDefault(sdk => string.Equals(sdk.Version.Raw, resolved, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return exact;
        }

        // Prefix resolution ("8.0") names a family, not a build: if any host
        // build matches the prefix, a higher foreign build must not win.
        bool PrefixMatches(SdkInstallation sdk) =>
            sdk.Version.Raw.StartsWith(resolved + ".", StringComparison.OrdinalIgnoreCase)
            || sdk.Version.Raw.StartsWith(resolved, StringComparison.OrdinalIgnoreCase)
               && resolved.Contains('.');

        if (_hostArchitecture is not null)
        {
            var hostMatch = ordered.FirstOrDefault(sdk =>
                PrefixMatches(sdk) && Architectures.MatchesHost(sdk.Architecture, _hostArchitecture));
            if (hostMatch is not null)
            {
                return hostMatch;
            }
        }

        return ordered.FirstOrDefault(PrefixMatches);
    }
}
