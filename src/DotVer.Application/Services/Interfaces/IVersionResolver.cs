using DotVer.Domain;

namespace DotVer.Application;

public interface IVersionResolver
{
    string ResolveToken(string token, DotVerConfig config, IReadOnlyList<SdkInstallation> installations);
    SdkInstallation? FindByToken(string token, DotVerConfig config, IReadOnlyList<SdkInstallation> installations);
}
