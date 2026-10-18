using System.Text.Json;
using DotVer.Application;
using DotVer.Domain;

namespace DotVer.Tests;

public sealed class GlobalJsonServiceTests
{
    [Fact]
    public void FindsGlobalJsonWalkingUpDirectoryTree()
    {
        using var sandbox = new TestSandbox();
        var repo = sandbox.CreateDirectory("repo");
        var src = Directory.CreateDirectory(Path.Combine(repo, "src", "app"));
        File.WriteAllText(
            Path.Combine(repo, "global.json"),
            """
            {
              "sdk": {
                "version": "8.0.412",
                "rollForward": "feature"
              }
            }
            """);

        var service = new GlobalJsonService();

        var request = service.TryFind(src.FullName);

        Assert.NotNull(request);
        Assert.Equal("8.0.412", request!.Version.Raw);
        Assert.Equal(SdkRollForward.Feature, request.RollForward);
        Assert.Equal(Path.Combine(repo, "global.json"), request.FilePath);
    }

    [Fact]
    public void WritePin_CreatePreservesUnknownProperties()
    {
        using var sandbox = new TestSandbox();
        var repo = sandbox.CreateDirectory("repo");
        File.WriteAllText(
            Path.Combine(repo, "global.json"),
            """
            {
              "msbuild-sdks": { "MySdk": "1.0.0" },
              "sdk": { "allowPrerelease": false }
            }
            """);

        var service = new GlobalJsonService();
        var result = service.WritePin(repo, SdkVersion.Parse("8.0.412"), SdkRollForward.LatestFeature);

        Assert.True(result.FileExisted);
        using var doc = JsonDocument.Parse(File.ReadAllText(result.FilePath));
        Assert.Equal("1.0.0", doc.RootElement.GetProperty("msbuild-sdks").GetProperty("MySdk").GetString());
        var sdk = doc.RootElement.GetProperty("sdk");
        Assert.Equal("8.0.412", sdk.GetProperty("version").GetString());
        Assert.Equal("latestFeature", sdk.GetProperty("rollForward").GetString());
        Assert.False(sdk.GetProperty("allowPrerelease").GetBoolean());
        Assert.Empty(Directory.EnumerateFiles(repo, "*.tmp"));
    }

    [Fact]
    public void WritePin_OnMalformedFile_RefusesToOverwrite()
    {
        using var sandbox = new TestSandbox();
        var repo = sandbox.CreateDirectory("repo");
        var path = Path.Combine(repo, "global.json");
        File.WriteAllText(path, "{ broken");
        var service = new GlobalJsonService();

        Assert.Throws<InvalidOperationException>(
            () => service.WritePin(repo, SdkVersion.Parse("8.0.412"), null));
        Assert.Equal("{ broken", File.ReadAllText(path));
    }

    [Fact]
    public void ClearPin_RemovesSdkPinAndKeepsOtherProperties()
    {
        using var sandbox = new TestSandbox();
        var repo = sandbox.CreateDirectory("repo");
        File.WriteAllText(
            Path.Combine(repo, "global.json"),
            """
            {
              "tools": { "dotnet": "8.0.412" },
              "sdk": { "version": "8.0.412", "rollForward": "feature" }
            }
            """);
        var service = new GlobalJsonService();

        var result = service.ClearPin(repo);

        Assert.True(result.Cleared);
        Assert.False(result.FileDeleted);
        using var doc = JsonDocument.Parse(File.ReadAllText(result.FilePath));
        Assert.False(doc.RootElement.TryGetProperty("sdk", out _));
        Assert.Equal("8.0.412", doc.RootElement.GetProperty("tools").GetProperty("dotnet").GetString());
    }

    [Fact]
    public void ClearPin_WhenFileOnlyHadPin_DeletesFile()
    {
        using var sandbox = new TestSandbox();
        var repo = sandbox.CreateDirectory("repo");
        var service = new GlobalJsonService();
        service.WritePin(repo, SdkVersion.Parse("8.0.412"), null);

        var result = service.ClearPin(repo);

        Assert.True(result.Cleared);
        Assert.True(result.FileDeleted);
        Assert.False(File.Exists(Path.Combine(repo, "global.json")));
    }

    [Fact]
    public void TryFind_OnMalformedJson_ReturnsNull()
    {
        using var sandbox = new TestSandbox();
        var repo = sandbox.CreateDirectory("repo");
        File.WriteAllText(Path.Combine(repo, "global.json"), "{ no");

        Assert.Null(new GlobalJsonService().TryFind(repo));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("""{"sdk":"x"}""")]
    [InlineData("""{"sdk":{"version":123}}""")]
    [InlineData("""{"sdk":{"version":"8.0.412","rollForward":1}}""")]
    [InlineData("""{"sdk":{"version":"99999999999.0.0"}}""")]
    public void TryFind_OnSemanticallyMalformedJson_ReturnsNullOrDefaults(string content)
    {
        using var sandbox = new TestSandbox();
        var repo = sandbox.CreateDirectory("repo");
        File.WriteAllText(Path.Combine(repo, "global.json"), content);

        var request = new GlobalJsonService().TryFind(repo);

        // rollForward:1 keeps the valid version with default policy; the rest yield no pin.
        if (content.Contains("\"rollForward\":1", StringComparison.Ordinal))
        {
            Assert.NotNull(request);
            Assert.Equal(SdkRollForward.Default, request!.RollForward);
        }
        else
        {
            Assert.Null(request);
        }
    }

    [Fact]
    public void ClearPin_FromSubdirectoryWithoutParentFlag_DoesNotMutateParent()
    {
        using var sandbox = new TestSandbox();
        var repo = sandbox.CreateDirectory("repo");
        var child = Directory.CreateDirectory(Path.Combine(repo, "src"));
        var service = new GlobalJsonService();
        service.WritePin(repo, SdkVersion.Parse("8.0.412"), null);
        var before = File.ReadAllText(Path.Combine(repo, "global.json"));

        Assert.Throws<InvalidOperationException>(() => service.ClearPin(child.FullName));
        Assert.Equal(before, File.ReadAllText(Path.Combine(repo, "global.json")));

        var result = service.ClearPin(child.FullName, allowParent: true);
        Assert.True(result.Cleared);
    }

    [Fact]
    public void ClearPin_WhenNoPinPresent_LeavesFileUntouched()
    {
        using var sandbox = new TestSandbox();
        var repo = sandbox.CreateDirectory("repo");
        var path = Path.Combine(repo, "global.json");
        const string content = """{"sdk":{"allowPrerelease":true}}""";
        File.WriteAllText(path, content);

        var result = new GlobalJsonService().ClearPin(repo);

        Assert.False(result.Cleared);
        Assert.Equal(content, File.ReadAllText(path));
    }
}
