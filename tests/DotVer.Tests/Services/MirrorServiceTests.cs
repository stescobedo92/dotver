using DotVer.Application;
using DotVer.Domain;

namespace DotVer.Tests;

public sealed class MirrorServiceTests
{
    [Fact]
    public async Task Set_StoresHttpsMirrorAndSourcesResolveFromIt()
    {
        var config = new InMemoryConfigStore(DotVerConfig.Empty);
        var service = new MirrorService(config);

        var mirror = await service.SetAsync("https://mirror.corp.example/dotnet/scripts/", "https://mirror.corp.example/dotnet/releases-index.json", CancellationToken.None);

        Assert.Equal("https://mirror.corp.example/dotnet/scripts", mirror.InstallScriptBaseUrl);
        var sources = await new ConfigDownloadSources(config).ResolveAsync(CancellationToken.None);
        Assert.True(sources.IsMirrored);
        Assert.Equal("https://mirror.corp.example/dotnet/scripts/dotnet-install.ps1", sources.PowerShellInstallScriptUrl);
        Assert.Equal("https://mirror.corp.example/dotnet/scripts/dotnet-install.sh", sources.ShellInstallScriptUrl);
        Assert.Equal("https://mirror.corp.example/dotnet/releases-index.json", sources.ReleasesIndexUrl);
        Assert.Equal(["mirror.corp.example"], sources.ExtraAllowedHosts);
    }

    [Theory]
    [InlineData("http://mirror.corp.example/dotnet")]
    [InlineData("https://user:pw@mirror.corp.example/dotnet")]
    [InlineData("ftp://mirror.corp.example/dotnet")]
    [InlineData("not a url")]
    public async Task Set_RejectsAnythingButPlainHttps(string url)
    {
        var config = new InMemoryConfigStore(DotVerConfig.Empty);
        var service = new MirrorService(config);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetAsync(url, null, CancellationToken.None));

        Assert.Null((await config.LoadAsync(CancellationToken.None)).Mirror);
    }

    [Fact]
    public async Task Clear_RestoresTheOfficialSources()
    {
        var config = new InMemoryConfigStore(DotVerConfig.Empty);
        var service = new MirrorService(config);
        await service.SetAsync("https://mirror.corp.example/dotnet", null, CancellationToken.None);

        Assert.True(await service.ClearAsync(CancellationToken.None));
        Assert.False(await service.ClearAsync(CancellationToken.None));

        var sources = await new ConfigDownloadSources(config).ResolveAsync(CancellationToken.None);
        Assert.False(sources.IsMirrored);
        Assert.Equal("https://dot.net/v1/dotnet-install.ps1", sources.PowerShellInstallScriptUrl);
        Assert.Equal("https://builds.dotnet.microsoft.com/dotnet/release-metadata/releases-index.json", sources.ReleasesIndexUrl);
        Assert.Empty(sources.ExtraAllowedHosts);
    }

    [Fact]
    public async Task Set_WithOnlyTheReleasesIndex_KeepsTheOfficialInstallScript()
    {
        var config = new InMemoryConfigStore(DotVerConfig.Empty);
        var service = new MirrorService(config);

        await service.SetAsync(null, "https://mirror.corp.example/releases-index.json", CancellationToken.None);

        var sources = await new ConfigDownloadSources(config).ResolveAsync(CancellationToken.None);
        Assert.Equal("https://dot.net/v1/dotnet-install.sh", sources.ShellInstallScriptUrl);
        Assert.Equal("https://mirror.corp.example/releases-index.json", sources.ReleasesIndexUrl);
    }
}
