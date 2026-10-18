using DotVer.Domain;

namespace DotVer.Tests;

public sealed class SdkVersionTests
{
    [Theory]
    [InlineData("8.0.100-a/../../shims")]
    [InlineData("8.0.100-x/../../../../../../Users/Public/pwn")]
    [InlineData("8.0.100-a\\..\\..\\shims")]
    [InlineData("8.0.100-a b")]
    [InlineData("8.0.100-a;b")]
    [InlineData("8.0.100-[red]")]
    [InlineData("8.0.100-..")]
    public void TryParse_RejectsTraversalAndControlSuffixes(string raw)
    {
        Assert.False(SdkVersion.TryParse(raw, out _));
    }

    [Theory]
    [InlineData("8.0.412")]
    [InlineData("10.0.100-preview.1.25120.13")]
    [InlineData("9.0.300-rc.1")]
    [InlineData("8.0.100+build.42")]
    public void TryParse_AcceptsValidVersions(string raw)
    {
        Assert.True(SdkVersion.TryParse(raw, out var version));
        Assert.Equal(raw, version!.Raw);
    }
}
