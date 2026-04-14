using DotVer.Domain;

namespace DotVer.Application;

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken cancellationToken);
}
