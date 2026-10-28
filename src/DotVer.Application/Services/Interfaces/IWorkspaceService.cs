using DotVer.Domain;

namespace DotVer.Application;

public interface IWorkspaceService
{
    Task<WorkspaceScanResult> ScanAsync(string root, CancellationToken cancellationToken);

    Task<WorkspaceEnsureResult> EnsureRecursiveAsync(string root, CancellationToken cancellationToken);

    /// <summary>Scans and reports the distinct SDK versions that would be installed, without installing.</summary>
    Task<WorkspaceEnsurePlan> PlanEnsureAsync(string root, CancellationToken cancellationToken);
}
