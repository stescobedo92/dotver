using DotVer.Application;
using DotVer.Domain;

namespace DotVer.Tests;

public sealed class InterceptorServiceTests
{
    [Fact]
    public async Task InterceptorHandlesVersionAndListSdksWithoutCallingRealDotnet()
    {
        using var sandbox = new TestSandbox(TestPlatform.Windows);
        sandbox.CreateSdkInstall("8.0.412", isManaged: true);
        sandbox.CreateSdkInstall("10.0.201", isManaged: false, rootName: "system-dotnet");

        var config = new InMemoryConfigStore(new DotVerConfig("8.0.412", null, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), []));
        var environment = sandbox.CreateEnvironment();
        var inventory = new InventoryService(DotVerPaths.From(environment), environment, config);
        var selectionService = new SelectionService(inventory, config, new GlobalJsonService(), new VersionResolver(), environment);
        var processRunner = new FakeProcessRunner();
        var interceptor = new InterceptorService(selectionService, inventory, processRunner);

        var versionResult = await interceptor.ExecuteAsync(["--version"], CancellationToken.None);
        var listResult = await interceptor.ExecuteAsync(["--list-sdks"], CancellationToken.None);

        Assert.Equal("8.0.412" + Environment.NewLine, versionResult.StandardOutput);
        Assert.Contains("10.0.201", listResult.StandardOutput, StringComparison.Ordinal);
        Assert.Empty(processRunner.Invocations);
    }
}
