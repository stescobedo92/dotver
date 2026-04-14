using DotVer.Domain;

namespace DotVer.Application;

public interface IEnvironmentBootstrapService
{
    Task<EnvironmentBootstrapResult> EnsureConfiguredAsync(CancellationToken cancellationToken);
}
