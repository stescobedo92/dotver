using DotVer.Domain;

namespace DotVer.Application;

public interface IInstallService
{
    Task<InstallPlan> ResolveInstallPlanAsync(string token, CancellationToken cancellationToken);
    Task<InstallOutcome> InstallAsync(string versionOrChannel, InstallOptions? options, CancellationToken cancellationToken);
    Task<RuntimeInstallOutcome> InstallRuntimeAsync(string versionOrChannel, string runtime, string? architecture, CancellationToken cancellationToken);
    Task<IReadOnlyList<ReleaseChannel>> ListRemoteAsync(CancellationToken cancellationToken);
}
