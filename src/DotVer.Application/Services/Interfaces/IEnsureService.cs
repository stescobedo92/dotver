using DotVer.Domain;

namespace DotVer.Application;

public interface IEnsureService
{
    Task<EnsureOutcome> EnsureAsync(bool setCurrent, string? directory, CancellationToken cancellationToken);
}
