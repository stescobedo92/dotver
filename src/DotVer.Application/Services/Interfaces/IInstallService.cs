using DotVer.Domain;

namespace DotVer.Application;

public interface IInstallService
{
    Task<InstallPlan> ResolveInstallPlanAsync(string token, CancellationToken cancellationToken);
    Task<InstallOutcome> InstallAsync(string versionOrChannel, CancellationToken cancellationToken);
    Task<IReadOnlyList<ReleaseChannel>> ListRemoteAsync(CancellationToken cancellationToken);
}
