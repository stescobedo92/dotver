using DotVer.Domain;

namespace DotVer.Application;

public interface IShimService
{
    Task<ShimInstallResult> InstallAsync(CancellationToken cancellationToken);
    string RenderEnvSnippet(ShellKind shellKind);
}
