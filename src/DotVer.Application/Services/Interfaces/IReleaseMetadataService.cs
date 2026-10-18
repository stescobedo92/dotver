using DotVer.Domain;

namespace DotVer.Application;

public interface IReleaseMetadataService
{
    Task<IReadOnlyList<ChannelRelease>> ListChannelReleasesAsync(string channel, CancellationToken cancellationToken);
}
