using DotVer.Domain;

namespace DotVer.Application;

public interface IInventoryService
{
    Task<IReadOnlyList<SdkInstallation>> ScanAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Destructive operations (uninstall, prune) must bypass any inventory
    /// cache so their view of the filesystem is always first-hand.
    /// </summary>
    Task<IReadOnlyList<SdkInstallation>> ScanAsync(bool forceRefresh, CancellationToken cancellationToken);

    string? GetSystemDotnetPath();
}
