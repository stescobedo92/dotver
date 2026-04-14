using DotVer.Domain;

namespace DotVer.Application;

public interface IInterceptorService
{
    Task<ProcessResult> ExecuteAsync(IReadOnlyList<string> args, CancellationToken cancellationToken);
}
