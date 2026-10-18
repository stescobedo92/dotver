using DotVer.Domain;

namespace DotVer.Application;

public interface IOutdatedService
{
    Task<IReadOnlyList<OutdatedEntry>> GetOutdatedAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<UpgradeOutcome>> UpgradeAsync(string? token, bool all, bool pruneOld, CancellationToken cancellationToken);
}
