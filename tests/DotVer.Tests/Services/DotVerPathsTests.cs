using DotVer.Application;

namespace DotVer.Tests;

public sealed class DotVerPathsTests
{
    [Fact]
    public void From_RelativeDotVerHome_YieldsFullyQualifiedPaths()
    {
        using var sandbox = new TestSandbox(TestPlatform.Linux);
        var home = Path.Combine(sandbox.Root, "home");
        Directory.CreateDirectory(home);
        var variables = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["DOTVER_HOME"] = "relative-dotver-home",
            ["PATH"] = string.Empty
        };
        var environment = new TestEnvironmentContext(home, sandbox.Root, Path.Combine(sandbox.Root, "dotver"), variables, TestPlatform.Linux);

        var paths = DotVerPaths.From(environment);

        // Inventory ignores relative root hints, so every dotver path must be
        // anchored once, here, instead of drifting with the working directory.
        Assert.True(Path.IsPathFullyQualified(paths.Home));
        Assert.True(Path.IsPathFullyQualified(paths.ManagedInstalls));
        Assert.EndsWith("relative-dotver-home", paths.Home, StringComparison.Ordinal);
    }
}
