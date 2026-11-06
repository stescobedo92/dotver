using DotVer.Domain;

namespace DotVer.Application;

public interface IInstallService
{
    Task<InstallPlan> ResolveInstallPlanAsync(string token, CancellationToken cancellationToken);
    Task<InstallOutcome> InstallAsync(string versionOrChannel, InstallOptions? options, CancellationToken cancellationToken);

    /// <summary>
    /// Installs several versions/channels in request order. A token that fails
    /// is reported and never stops the next one; a token that resolves to a
    /// version an earlier token already produced is marked as a duplicate.
    /// </summary>
    Task<MultiInstallResult> InstallManyAsync(IReadOnlyList<string> tokens, InstallOptions? options, IProgress<string>? progress, CancellationToken cancellationToken);

    /// <summary>Installs an exact SDK version from a local .zip/.tar.gz whose digest the caller verified out of band.</summary>
    Task<InstallOutcome> InstallFromArchiveAsync(string version, string archivePath, ArchiveChecksum checksum, InstallOptions? options, CancellationToken cancellationToken);
    Task<RuntimeInstallOutcome> InstallRuntimeAsync(string versionOrChannel, string runtime, string? architecture, CancellationToken cancellationToken);
    Task<IReadOnlyList<ReleaseChannel>> ListRemoteAsync(CancellationToken cancellationToken);
}
