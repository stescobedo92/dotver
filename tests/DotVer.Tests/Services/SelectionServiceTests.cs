using DotVer.Application;
using DotVer.Domain;

namespace DotVer.Tests;

public sealed class SelectionServiceTests
{
    [Fact]
    public async Task GlobalJsonWinsOverCurrentVersion()
    {
        using var sandbox = new TestSandbox(TestPlatform.Windows);
        var managedInstall = sandbox.CreateSdkInstall("8.0.412", isManaged: true);
        sandbox.CreateSdkInstall("10.0.201", isManaged: true);
        File.WriteAllText(
            Path.Combine(sandbox.Root, "global.json"),
            """
            {
              "sdk": {
                "version": "10.0.201"
              }
            }
            """);

        var config = new InMemoryConfigStore(new DotVerConfig("8.0.412", "8.0.412", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), []));
        var environment = sandbox.CreateEnvironment();
        var inventory = new InventoryService(DotVerPaths.From(environment), environment, config);
        var selectionService = new SelectionService(inventory, config, new GlobalJsonService(), new VersionResolver(), environment);

        var selection = await selectionService.ResolveAsync(CancellationToken.None);

        Assert.NotNull(selection.Installation);
        Assert.Equal("10.0.201", selection.Installation!.Version.Raw);
        Assert.True(selection.FromGlobalJson);
        Assert.DoesNotContain(managedInstall.Version.Raw, selection.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnmatchedGlobalJsonFallsBackToCurrentVersion()
    {
        using var sandbox = new TestSandbox(TestPlatform.Windows);
        sandbox.CreateSdkInstall("8.0.412", isManaged: true);
        File.WriteAllText(
            Path.Combine(sandbox.Root, "global.json"),
            """
            {
              "sdk": {
                "version": "9.0.999"
              }
            }
            """);

        var config = new InMemoryConfigStore(new DotVerConfig("8.0.412", null, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), []));
        var environment = sandbox.CreateEnvironment();
        var inventory = new InventoryService(DotVerPaths.From(environment), environment, config);
        var selectionService = new SelectionService(inventory, config, new GlobalJsonService(), new VersionResolver(), environment);

        var selection = await selectionService.ResolveAsync(CancellationToken.None);

        Assert.NotNull(selection.Installation);
        Assert.Equal("8.0.412", selection.Installation!.Version.Raw);
        Assert.False(selection.FromGlobalJson);
    }

    [Fact]
    public async Task DotVerVersionOverrideWinsOverGlobalJson()
    {
        using var sandbox = new TestSandbox(TestPlatform.Windows);
        sandbox.CreateSdkInstall("8.0.412", isManaged: true);
        sandbox.CreateSdkInstall("10.0.201", isManaged: true);
        File.WriteAllText(
            Path.Combine(sandbox.Root, "global.json"),
            """{"sdk":{"version":"10.0.201"}}""");

        var config = new InMemoryConfigStore(DotVerConfig.Empty);
        var environment = sandbox.CreateEnvironment();
        environment.SetUserEnvironmentVariable("DOTVER_VERSION", "8.0.412");
        var inventory = new InventoryService(DotVerPaths.From(environment), environment, config);
        var selectionService = new SelectionService(inventory, config, new GlobalJsonService(), new VersionResolver(), environment);

        var selection = await selectionService.ResolveAsync(CancellationToken.None);

        Assert.Equal("8.0.412", selection.Installation?.Version.Raw);
        Assert.Contains("DOTVER_VERSION", selection.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("lts")]
    [InlineData("8.0")]
    [InlineData("9.0.999")]
    public async Task DotVerVersionOverride_NonExactOrMissing_FailsLoudly(string overrideValue)
    {
        using var sandbox = new TestSandbox(TestPlatform.Windows);
        sandbox.CreateSdkInstall("8.0.412", isManaged: true);

        var config = new InMemoryConfigStore(new DotVerConfig(
            null,
            null,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["lts"] = "8.0.412" },
            []));
        var environment = sandbox.CreateEnvironment();
        environment.SetUserEnvironmentVariable("DOTVER_VERSION", overrideValue);
        var inventory = new InventoryService(DotVerPaths.From(environment), environment, config);
        var selectionService = new SelectionService(inventory, config, new GlobalJsonService(), new VersionResolver(), environment);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => selectionService.ResolveAsync(CancellationToken.None));
    }

    [Fact]
    public async Task MalformedGlobalJsonDoesNotBreakResolution()
    {
        using var sandbox = new TestSandbox(TestPlatform.Windows);
        sandbox.CreateSdkInstall("8.0.412", isManaged: true);
        File.WriteAllText(Path.Combine(sandbox.Root, "global.json"), "{ this is not json");

        var config = new InMemoryConfigStore(new DotVerConfig("8.0.412", null, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), []));
        var environment = sandbox.CreateEnvironment();
        var inventory = new InventoryService(DotVerPaths.From(environment), environment, config);
        var selectionService = new SelectionService(inventory, config, new GlobalJsonService(), new VersionResolver(), environment);

        var selection = await selectionService.ResolveAsync(CancellationToken.None);

        Assert.Equal("8.0.412", selection.Installation?.Version.Raw);
    }
}
