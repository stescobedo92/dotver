using DotVer.Domain;

namespace DotVer.Application;

public interface IMatrixService
{
    Task<MatrixResult> RunAsync(IReadOnlyList<string> tokens, IReadOnlyList<string> command, bool parallel, CancellationToken cancellationToken);
}
