using DotVer.Domain;

namespace DotVer.Application;

public interface IConfigStore
{
    Task<DotVerConfig> LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(DotVerConfig config, CancellationToken cancellationToken);
}
