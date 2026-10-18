using DotVer.Domain;

namespace DotVer.Application;

public interface IExecService
{
    Task<SdkInstallation> ResolveSdkAsync(string token, CancellationToken cancellationToken);

    Task<ProcessResult> ExecuteAsync(string token, IReadOnlyList<string> command, CancellationToken cancellationToken);
}
