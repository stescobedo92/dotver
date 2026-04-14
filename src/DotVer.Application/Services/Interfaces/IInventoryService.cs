using DotVer.Domain;

namespace DotVer.Application;

public interface IInventoryService
{
    Task<IReadOnlyList<SdkInstallation>> ScanAsync(CancellationToken cancellationToken);
    string? GetSystemDotnetPath();
}
