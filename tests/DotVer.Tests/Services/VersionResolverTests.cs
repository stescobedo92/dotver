using DotVer.Application;
using DotVer.Domain;

namespace DotVer.Tests;

public sealed class VersionResolverTests
{
    [Fact]
    public void ResolvesAliasAndLatestAgainstInstalledInventory()
    {
        var config = new DotVerConfig(
            null,
            null,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["lts"] = "8.0.412"
            },
            []);

        var installations = new[]
        {
            MakeInstallation("8.0.412", true),
            MakeInstallation("10.0.201", true)
        };

        var resolver = new VersionResolver();

        Assert.Equal("8.0.412", resolver.ResolveToken("lts", config, installations));
        Assert.Equal("10.0.201", resolver.ResolveToken("latest", config, installations));
    }

    [Fact]
    public void ResolvesExternalSdkForUse()
    {
        var config = DotVerConfig.Empty;
        var installations = new[]
        {
            MakeInstallation("6.0.428", false, canBeUsedDirectly: false),
            MakeInstallation("8.0.412", true)
        };

        var resolver = new VersionResolver();
        var sdk = resolver.FindByToken("6.0.428", config, installations);

        Assert.NotNull(sdk);
        Assert.Equal("6.0.428", sdk!.Version.Raw);
    }

    private static SdkInstallation MakeInstallation(string version, bool isManaged, bool canBeUsedDirectly = true) =>
        new(SdkVersion.Parse(version), $"/fake/{version}", $"/fake/{version}/sdk/{version}", $"/fake/{version}/dotnet", isManaged ? SdkOriginKind.ManagedInstall : SdkOriginKind.OfficialInstallRoot, "test", isManaged, canBeUsedDirectly, "test");
}
