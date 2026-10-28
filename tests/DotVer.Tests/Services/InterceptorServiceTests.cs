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
        var processRunner = new FakeProcessRunner();
        var interceptor = CreateInterceptor(environment, config, processRunner);

        var versionResult = await interceptor.ExecuteAsync(["--version"], CancellationToken.None);
        var listResult = await interceptor.ExecuteAsync(["--list-sdks"], CancellationToken.None);

        Assert.Equal("8.0.412" + Environment.NewLine, versionResult.StandardOutput);
        Assert.Contains("10.0.201", listResult.StandardOutput, StringComparison.Ordinal);
        Assert.Empty(processRunner.Invocations);
    }

    [Fact]
    public async Task Intercept_WithoutSelectableSdkOrSystemHost_FailsInsteadOfSpawningBareDotnet()
    {
        using var sandbox = new TestSandbox(TestPlatform.Linux);
        // A cloned repo (the cwd) plants its own `dotnet`: it must never run.
        File.WriteAllText(Path.Combine(sandbox.Root, "dotnet"), "#!/bin/sh\necho pwned\n");
        var environment = sandbox.CreateEnvironment();
        var processRunner = new FakeProcessRunner();
        var interceptor = CreateInterceptor(environment, new InMemoryConfigStore(DotVerConfig.Empty), processRunner);

        var result = await interceptor.ExecuteAsync(["build"], CancellationToken.None);

        Assert.Empty(processRunner.Invocations);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("No .NET SDK", result.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Intercept_FallsBackToDotnetOnPath_SkippingShimsAndCurrentDirectory()
    {
        using var sandbox = new TestSandbox(TestPlatform.Linux);
        var environment = sandbox.CreateEnvironment();
        var paths = DotVerPaths.From(environment);
        Directory.CreateDirectory(paths.Shims);
        File.WriteAllText(Path.Combine(paths.Shims, "dotnet"), "shim");
        File.WriteAllText(Path.Combine(sandbox.Root, "dotnet"), "cwd");
        var tools = sandbox.CreateDirectory("tools");
        var realHost = Path.Combine(tools, "dotnet");
        File.WriteAllText(realHost, "host");
        environment.SetUserEnvironmentVariable("PATH", string.Join(Path.PathSeparator, paths.Shims, sandbox.Root, tools));
        var processRunner = new FakeProcessRunner();
        var interceptor = CreateInterceptor(environment, new InMemoryConfigStore(DotVerConfig.Empty), processRunner);

        var result = await interceptor.ExecuteAsync(["build"], CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        var spec = Assert.Single(processRunner.Invocations);
        Assert.Equal(realHost, spec.FileName);
        Assert.True(Path.IsPathFullyQualified(spec.FileName));
    }

    private static InterceptorService CreateInterceptor(TestEnvironmentContext environment, InMemoryConfigStore config, FakeProcessRunner processRunner)
    {
        var paths = DotVerPaths.From(environment);
        var inventory = new InventoryService(paths, environment, config);
        var selectionService = new SelectionService(inventory, config, new GlobalJsonService(), new VersionResolver(), environment);
        return new InterceptorService(selectionService, inventory, processRunner, environment, paths);
    }
}
