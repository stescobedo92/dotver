using DotVer.Domain;

namespace DotVer.Application;

public interface IInfoService
{
    Task<SdkInfo> GetInfoAsync(string? token, CancellationToken cancellationToken);
}
