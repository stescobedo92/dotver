using DotVer.Application;
using DotVer.Domain;

namespace DotVer.Tests;

public sealed class HostPlatformResolverTests
{
    [Theory]
    [InlineData(TestPlatform.Windows, "win-x64", ".zip")]
    [InlineData(TestPlatform.Linux, "linux-x64", ".tar.gz")]
    [InlineData(TestPlatform.MacOS, "osx-x64", ".tar.gz")]
    public void Resolve_BuildsTheRidTheHostWouldReport(TestPlatform platform, string rid, string extension)
    {
        using var sandbox = new TestSandbox(platform);
        var environment = sandbox.CreateEnvironment();

        var resolved = HostPlatformResolver.Resolve(environment, fileExists: static _ => false);

        Assert.Equal(rid, resolved.Rid);
        Assert.Equal(extension, resolved.ArchiveExtension);
        Assert.Equal("x64", resolved.Architecture);
    }

    [Fact]
    public void Resolve_OnLinux_DetectsMuslThroughTheLoaderPath()
    {
        using var sandbox = new TestSandbox(TestPlatform.Linux);
        var environment = sandbox.CreateEnvironment();
        var probed = new List<string>();

        var resolved = HostPlatformResolver.Resolve(environment, path =>
        {
            probed.Add(path);
            return path == "/lib/ld-musl-x86_64.so.1";
        });

        Assert.Equal("linux-musl-x64", resolved.Rid);
        Assert.Equal(HostPlatform.LinuxMuslOs, resolved.Os);
        Assert.Contains("/lib/ld-musl-x86_64.so.1", probed);
    }

    [Theory]
    [InlineData("linux-musl-arm64", "linux-musl", "arm64", ".tar.gz")]
    [InlineData("WIN-X64", "win", "x64", ".zip")]
    [InlineData("osx-arm64", "osx", "arm64", ".tar.gz")]
    [InlineData("freebsd-x64", "freebsd", "x64", ".tar.gz")]
    [InlineData("linux-riscv64", "linux", "riscv64", ".tar.gz")]
    public void TryParseRid_SplitsOsAndArchitectureLongestOsFirst(string rid, string os, string architecture, string extension)
    {
        Assert.True(HostPlatformResolver.TryParseRid(rid, out var platform));
        Assert.Equal(os, platform!.Os);
        Assert.Equal(architecture, platform.Architecture);
        Assert.Equal(extension, platform.ArchiveExtension);
        Assert.Equal(rid.ToLowerInvariant(), platform.Rid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("linux")]
    [InlineData("x64")]
    [InlineData("windows-x64")]
    [InlineData("win-mips")]
    [InlineData("win-s390x")]
    [InlineData("linux-x64-extra")]
    [InlineData("../linux-x64")]
    public void TryParseRid_RejectsAnythingThatIsNotAPublishedRid(string rid)
    {
        Assert.False(HostPlatformResolver.TryParseRid(rid, out var platform));
        Assert.Null(platform);
        Assert.Throws<InvalidOperationException>(() => HostPlatformResolver.FromRid(rid));
    }

    [Theory]
    [InlineData("windows", "amd64", "win-x64")]
    [InlineData("linux", "musl-arm64", "linux-musl-arm64")]
    [InlineData("macos", "aarch64", "osx-arm64")]
    [InlineData("alpine", "x86_64", "linux-musl-x64")]
    public void FromParts_NormalizesTheSpellingsTheScriptsAccept(string os, string architecture, string rid)
    {
        Assert.Equal(rid, HostPlatformResolver.FromParts(os, architecture).Rid);
    }

    [Fact]
    public void FromParts_RejectsArchitecturesWindowsIsNotPublishedFor()
    {
        Assert.Throws<InvalidOperationException>(() => HostPlatformResolver.FromParts("win", "s390x"));
    }

    [Fact]
    public void SupportedRids_AllParseBackToThemselves()
    {
        foreach (var rid in HostPlatformResolver.SupportedRids)
        {
            Assert.True(HostPlatformResolver.TryParseRid(rid, out var platform), rid);
            Assert.Equal(rid, platform!.Rid);
        }
    }

    [Fact]
    public void MissingRuntimeUrl_MatchesTheQueryStringTheHostBuilds()
    {
        Assert.Equal(
            "https://aka.ms/dotnet-core-applaunch?missing_runtime=true&arch=x64&rid=win-x64&os=win10",
            DotnetHostLinks.MissingRuntimeUrl(null, null, "x64", "win-x64", "win10"));

        Assert.Equal(
            "https://aka.ms/dotnet-core-applaunch?framework=Microsoft.NETCore.App&framework_version=8.0.0&arch=x64&rid=win-x64&os=win11&apphost_version=8.0.33",
            DotnetHostLinks.MissingRuntimeUrl("Microsoft.NETCore.App", "8.0.0", "x64", "win-x64", "win11", "8.0.33"));
    }
}
