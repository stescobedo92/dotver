using DotVer.Application;

namespace DotVer.Tests;

public sealed class ExecutableLocatorTests
{
    [Fact]
    public void FindOnPath_SkipsCurrentDirectoryAndRelativeEntries()
    {
        using var sandbox = new TestSandbox(TestPlatform.Windows);
        var repo = sandbox.CreateDirectory("repo");
        var tools = sandbox.CreateDirectory("tools");
        File.WriteAllText(Path.Combine(repo, "pwsh.exe"), string.Empty);
        File.WriteAllText(Path.Combine(tools, "pwsh.exe"), string.Empty);

        var pathValue = string.Join(Path.PathSeparator, repo, ".", tools);
        var environment = new TestEnvironmentContext(
            Path.Combine(sandbox.Root, "home"),
            repo,
            Path.Combine(sandbox.Root, "dotver.exe"),
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase) { ["PATH"] = pathValue },
            TestPlatform.Windows);

        var found = ExecutableLocator.FindOnPath(environment, "pwsh.exe");

        Assert.Equal(Path.Combine(tools, "pwsh.exe"), found);
    }
}
