using DotVer.Domain;

namespace DotVer.Application;

public interface IStateService
{
    Task<DotVerConfig> GetConfigAsync(CancellationToken cancellationToken);
    Task<SdkInstallation> SetCurrentAsync(string token, CancellationToken cancellationToken);
    Task<SdkInstallation> SetCurrentAndDefaultAsync(string token, CancellationToken cancellationToken);
    Task<SdkInstallation> SetDefaultAsync(string token, CancellationToken cancellationToken);
    Task AddAliasAsync(string alias, string versionToken, CancellationToken cancellationToken);
    Task RemoveAliasAsync(string alias, CancellationToken cancellationToken);
    Task<string> RegisterRootAsync(string path, CancellationToken cancellationToken);
    Task<UninstallOutcome> UninstallManagedAsync(string token, CancellationToken cancellationToken);
    Task<UninstallOutcome> UninstallManagedAsync(SdkInstallation target, CancellationToken cancellationToken);
    Task<RepointResult> RepointVersionAsync(string fromVersion, string toVersion, CancellationToken cancellationToken);
    Task<bool> ClearCurrentAsync(CancellationToken cancellationToken);
    Task<bool> ClearDefaultAsync(CancellationToken cancellationToken);
}
