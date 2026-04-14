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
    }
}
