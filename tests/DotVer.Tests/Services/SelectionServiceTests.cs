using DotVer.Application;
using DotVer.Domain;

namespace DotVer.Tests;

public sealed class SelectionServiceTests
{
    [Fact]
    public async Task CurrentVersionWinsOverGlobalJson()
    {
        using var sandbox = new TestSandbox(TestPlatform.Windows);
        var managedInstall = sandbox.CreateSdkInstall("8.0.412", isManaged: true);
        var officialInstall = sandbox.CreateSdkInstall("10.0.201", isManaged: false, rootName: "system-dotnet");
        File.WriteAllText(
            Path.Combine(sandbox.Root, "global.json"),
            """
            {
              "sdk": {
                "version": "10.0.201"
              }
            }
            """);

        var config = new InMemoryConfigStore(new DotVerConfig("8.0.412", null, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), []));
        var environment = sandbox.CreateEnvironment();
        var inventory = new InventoryService(DotVerPaths.From(environment), environment, config);
        var selectionService = new SelectionService(inventory, config, new GlobalJsonService(), new VersionResolver(), environment);

        var selection = await selectionService.ResolveAsync(CancellationToken.None);

        Assert.NotNull(selection.Installation);
        Assert.Equal(managedInstall.Version.Raw, selection.Installation!.Version.Raw);
        Assert.DoesNotContain(officialInstall.Version.Raw, selection.Reason, StringComparison.OrdinalIgnoreCase);
    }
}
