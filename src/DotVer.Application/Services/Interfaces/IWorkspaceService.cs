using DotVer.Domain;

namespace DotVer.Application;

public interface IWorkspaceService
{
    Task<WorkspaceScanResult> ScanAsync(string root, CancellationToken cancellationToken);

    Task<WorkspaceEnsureResult> EnsureRecursiveAsync(string root, CancellationToken cancellationToken);
}
