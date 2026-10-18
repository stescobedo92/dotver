using DotVer.Application;
using DotVer.Domain;

namespace DotVer.Tests;

public sealed class StateServiceTests
{
    [Fact]
    public async Task UninstallManagedSdkRepairsCurrentDefaultAndAliases()
    {
        using var sandbox = new TestSandbox(TestPlatform.Windows);
        sandbox.CreateSdkInstall("8.0.412", isManaged: true);
        sandbox.CreateSdkInstall("7.0.410", isManaged: true);

        var configStore = new InMemoryConfigStore(new DotVerConfig(
            "8.0.412",
            "8.0.412",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["lts"] = "8.0.412"
            },
            []));
        var environment = sandbox.CreateEnvironment();
        var inventory = new InventoryService(DotVerPaths.From(environment), environment, configStore);
        var stateService = new StateService(configStore, new VersionResolver(), inventory, DotVerPaths.From(environment), environment);

        var outcome = await stateService.UninstallManagedAsync("8.0.412", CancellationToken.None);

        Assert.Equal("8.0.412", outcome.RemovedVersion);
        Assert.Equal("7.0.410", outcome.NewCurrentVersion);
        Assert.Equal("7.0.410", outcome.NewDefaultVersion);
        Assert.Contains("lts", outcome.RemovedAliases);
    }

    [Fact]
    public async Task UninstallManaged_FallbackNeverLandsOnForeignArchitecture()
    {
        using var sandbox = new TestSandbox(TestPlatform.Linux);
        sandbox.CreateSdkInstall("8.0.412", isManaged: true);
        var foreignRoot = Path.Combine(sandbox.Root, ".dotver", "installs", "9.0.100--arm64");
        Directory.CreateDirectory(Path.Combine(foreignRoot, "sdk", "9.0.100"));
        File.WriteAllText(Path.Combine(foreignRoot, "dotnet"), string.Empty);

        var configStore = new InMemoryConfigStore(new DotVerConfig(
            "8.0.412",
            "8.0.412",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            []));
        var environment = sandbox.CreateEnvironment();
        var inventory = new InventoryService(DotVerPaths.From(environment), environment, configStore);
        var stateService = new StateService(configStore, new VersionResolver(), inventory, DotVerPaths.From(environment), environment);

        var outcome = await stateService.UninstallManagedAsync("8.0.412", CancellationToken.None);

        Assert.Null(outcome.NewCurrentVersion);
        Assert.Null(outcome.NewDefaultVersion);
    }

    [Fact]
    public async Task UninstallManaged_OutsideManagedRoot_RefusesRecursiveDelete()
    {
        using var sandbox = new TestSandbox(TestPlatform.Windows);
        var externalRoot = Path.Combine(sandbox.Root, "external", "8.0.412");
        Directory.CreateDirectory(Path.Combine(externalRoot, "sdk", "8.0.412"));
        var marker = Path.Combine(externalRoot, "keep.txt");
        File.WriteAllText(marker, "keep");

        var fakeSdk = new SdkInstallation(
            SdkVersion.Parse("8.0.412"),
            externalRoot,
            Path.Combine(externalRoot, "sdk", "8.0.412"),
            Path.Combine(externalRoot, "dotnet.exe"),
            SdkOriginKind.ManagedInstall,
            "test",
            IsManaged: true,
            CanBeUsedDirectly: true,
            "test");

        var configStore = new InMemoryConfigStore(DotVerConfig.Empty);
        var environment = sandbox.CreateEnvironment();
        var stateService = new StateService(
            configStore,
            new VersionResolver(),
            new SingleSdkInventory(fakeSdk),
            DotVerPaths.From(environment),
            environment);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => stateService.UninstallManagedAsync("8.0.412", CancellationToken.None));
        Assert.True(File.Exists(marker));
    }

    private sealed class SingleSdkInventory(SdkInstallation sdk) : IInventoryService
    {
        public Task<IReadOnlyList<SdkInstallation>> ScanAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SdkInstallation>>([sdk]);

        public string? GetSystemDotnetPath() => null;
    }
}
