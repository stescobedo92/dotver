using DotVer.Domain;

namespace DotVer.Application;

public interface IPruneService
{
    Task<PruneResult> PruneAsync(bool keepLatestPerChannel, bool execute, CancellationToken cancellationToken);
}
