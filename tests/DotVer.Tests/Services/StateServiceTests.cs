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
        var stateService = new StateService(configStore, new VersionResolver(), inventory, DotVerPaths.From(environment));

        var outcome = await stateService.UninstallManagedAsync("8.0.412", CancellationToken.None);

        Assert.Equal("8.0.412", outcome.RemovedVersion);
        Assert.Equal("7.0.410", outcome.NewCurrentVersion);
        Assert.Equal("7.0.410", outcome.NewDefaultVersion);
        Assert.Contains("lts", outcome.RemovedAliases);
    }
}
