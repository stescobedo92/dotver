using DotVer.Application;
using DotVer.Domain;

namespace DotVer.Tests;

public sealed class InventoryServiceTests
{
    [Fact]
    public async Task Scan_IgnoresRelativeRootHintsFromConfigPathAndDotnetRoot()
    {
        using var sandbox = new TestSandbox(TestPlatform.Linux);

        // A cloned repo that looks like a dotnet root (host + sdk/<version>),
        // planted under the process cwd because that is what Path.GetFullPath
        // resolves relative hints against. GetRelativePath cannot express a
        // path on another drive, so the sandbox is not usable for this.
        var planted = Path.Combine(Directory.GetCurrentDirectory(), "planted-root-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(planted, "sdk", "9.0.999"));
        File.WriteAllText(Path.Combine(planted, "dotnet"), string.Empty);
        try
        {
            // Exactly what `register-root .`, a `.` entry in PATH or DOTNET_ROOT=sdk look like.
            var relative = Path.GetFileName(planted);
            Assert.False(Path.IsPathFullyQualified(relative));

            var config = new InMemoryConfigStore(new DotVerConfig(null, null, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), [relative]));
            var environment = sandbox.CreateEnvironment();
            environment.SetUserEnvironmentVariable("PATH", relative);
            environment.SetUserEnvironmentVariable("DOTNET_ROOT", relative);
            var inventory = new InventoryService(DotVerPaths.From(environment), environment, config);

            var installations = await inventory.ScanAsync(CancellationToken.None);

            Assert.DoesNotContain(installations, sdk => sdk.Version.Raw == "9.0.999");
        }
        finally
        {
            Directory.Delete(planted, recursive: true);
        }
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
