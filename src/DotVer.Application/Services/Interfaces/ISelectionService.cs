using DotVer.Domain;

namespace DotVer.Application;

public interface ISelectionService
{
    Task<SdkSelection> ResolveAsync(CancellationToken cancellationToken);
}
