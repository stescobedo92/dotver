using DotVer.Domain;

namespace DotVer.Application;

public interface IEnvironmentBootstrapService
{
    Task<EnvironmentBootstrapResult> EnsureConfiguredAsync(CancellationToken cancellationToken);

    /// <summary>Profiles that currently carry a dotver block (recorded ones plus the standard candidates).</summary>
    IReadOnlyList<string> FindConfiguredProfiles();

    /// <summary>Removes dotver blocks from profiles and, on Windows, the shims PATH entry and DOTVER_HOME from the user environment.</summary>
    Task<EnvironmentRemovalResult> RemoveAsync(CancellationToken cancellationToken);
}
