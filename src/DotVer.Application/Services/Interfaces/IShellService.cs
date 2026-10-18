using DotVer.Domain;

namespace DotVer.Application;

public interface IShellService
{
    Task<ProcessResult> SpawnShellAsync(string token, string? shellOverride, CancellationToken cancellationToken);
}
