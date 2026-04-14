using DotVer.Domain;

namespace DotVer.Application;

public sealed class VersionResolver : IVersionResolver
{
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
            return installations
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
        var ordered = installations.OrderByDescending(static sdk => sdk.Version);

        return ordered.FirstOrDefault(sdk => string.Equals(sdk.Version.Raw, resolved, StringComparison.OrdinalIgnoreCase))
            ?? ordered.FirstOrDefault(sdk => sdk.Version.Raw.StartsWith(resolved + ".", StringComparison.OrdinalIgnoreCase)
                                          || sdk.Version.Raw.StartsWith(resolved, StringComparison.OrdinalIgnoreCase)
                                             && resolved.Contains('.'));
    }
}
