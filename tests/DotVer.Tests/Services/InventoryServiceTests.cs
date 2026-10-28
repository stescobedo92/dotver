using DotVer.Application;
using DotVer.Domain;

namespace DotVer.Tests;

public sealed class InventoryServiceTests
{
    [Fact]
    public async Task Scan_IgnoresRelativeRootHintsFromConfigPathAndDotnetRoot()
    {
        using var sandbox = new TestSandbox(TestPlatform.Linux);
        // A cloned repo that looks like a dotnet root: host + sdk/<version>.
        var planted = sandbox.CreateDirectory("planted-root");
        Directory.CreateDirectory(Path.Combine(planted, "sdk", "9.0.999"));
        File.WriteAllText(Path.Combine(planted, "dotnet"), string.Empty);

        // Relative to the process cwd (what Path.GetFullPath resolves against),
        // exactly like `register-root .` or a `.` entry in PATH would be.
        var relative = Path.GetRelativePath(Directory.GetCurrentDirectory(), planted);
        Assert.False(Path.IsPathFullyQualified(relative));

        var config = new InMemoryConfigStore(new DotVerConfig(null, null, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), [relative]));
        var environment = sandbox.CreateEnvironment();
        environment.SetUserEnvironmentVariable("PATH", relative);
        environment.SetUserEnvironmentVariable("DOTNET_ROOT", relative);
        var inventory = new InventoryService(DotVerPaths.From(environment), environment, config);

        var installations = await inventory.ScanAsync(CancellationToken.None);

        Assert.DoesNotContain(installations, sdk => sdk.Version.Raw == "9.0.999");
    }

    [Fact]
    public async Task Scan_StillHonorsAbsoluteRegisteredRoots()
    {
        using var sandbox = new TestSandbox(TestPlatform.Linux);
        var custom = sandbox.CreateDirectory("custom-root");
        Directory.CreateDirectory(Path.Combine(custom, "sdk", "9.0.100"));
        File.WriteAllText(Path.Combine(custom, "dotnet"), string.Empty);

        var config = new InMemoryConfigStore(new DotVerConfig(null, null, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), [custom]));
        var environment = sandbox.CreateEnvironment();
        var inventory = new InventoryService(DotVerPaths.From(environment), environment, config);

        var installations = await inventory.ScanAsync(CancellationToken.None);

        Assert.Contains(installations, sdk => sdk.Version.Raw == "9.0.100" && sdk.Origin == SdkOriginKind.RegisteredRoot);
    }
}
