using DotVer.Application;
using DotVer.Domain;

namespace DotVer.Tests;

public sealed class ExecServiceTests
{
    [Fact]
    public async Task Execute_DotnetCommand_UsesSelectedHostAndEnvironment()
    {
        using var sandbox = new TestSandbox(TestPlatform.Windows);
        var sdk = sandbox.CreateSdkInstall("8.0.412", isManaged: true);
        var harness = new Harness(sandbox, [sdk], new ProcessResult(7, string.Empty, string.Empty));

        var result = await harness.Service.ExecuteAsync("8.0", ["dotnet", "build", "-c", "Release"], CancellationToken.None);

        Assert.Equal(7, result.ExitCode);
        var spec = Assert.Single(harness.Process.Invocations);
        Assert.Equal(sdk.DotnetPath, spec.FileName);
        Assert.Equal(["build", "-c", "Release"], spec.Arguments);
        Assert.True(spec.InheritStandardIo);
        Assert.NotNull(spec.EnvironmentVariables);
        Assert.Equal(sdk.HostRoot, spec.EnvironmentVariables!["DOTNET_ROOT"]);
        Assert.Equal("8.0.412", spec.EnvironmentVariables["DOTVER_VERSION"]);
    }

    [Fact]
    public async Task Execute_UnknownToken_ThrowsWithoutRunning()
    {
        using var sandbox = new TestSandbox(TestPlatform.Windows);
        var harness = new Harness(sandbox, [], new ProcessResult(0, string.Empty, string.Empty));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => harness.Service.ExecuteAsync("9.0.999", ["dotnet", "--version"], CancellationToken.None));
        Assert.Empty(harness.Process.Invocations);
    }

    [Fact]
    public async Task Execute_BareCommandNotOnPath_ThrowsWithoutRunning()
    {
        using var sandbox = new TestSandbox(TestPlatform.Windows);
        var sdk = sandbox.CreateSdkInstall("8.0.412", isManaged: true);
        var harness = new Harness(sandbox, [sdk], new ProcessResult(0, string.Empty, string.Empty));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => harness.Service.ExecuteAsync("8.0.412", ["definitely-not-a-real-tool-xyz"], CancellationToken.None));
        Assert.Empty(harness.Process.Invocations);
    }

    [Fact]
    public async Task ResolveSdk_ByAlias_Works()
    {
        using var sandbox = new TestSandbox(TestPlatform.Windows);
        var sdk = sandbox.CreateSdkInstall("8.0.412", isManaged: true);
        var config = new DotVerConfig(
            null,
            null,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["lts"] = "8.0.412" },
            []);
        var harness = new Harness(sandbox, [sdk], new ProcessResult(0, string.Empty, string.Empty), config);

        var resolved = await harness.Service.ResolveSdkAsync("lts", CancellationToken.None);

        Assert.Equal("8.0.412", resolved.Version.Raw);
    }

    private sealed class Harness
    {
        public Harness(TestSandbox sandbox, IReadOnlyList<SdkInstallation> sdks, ProcessResult result, DotVerConfig? config = null)
        {
            var environment = sandbox.CreateEnvironment();
            Process = new RecordingProcessRunner(result);
            Service = new ExecService(
                new FakeInventory(sdks),
                new InMemoryConfigStore(config ?? DotVerConfig.Empty),
                new VersionResolver(),
                Process,
                environment);
        }

        public ExecService Service { get; }
        public RecordingProcessRunner Process { get; }
    }

    private sealed class FakeInventory(IReadOnlyList<SdkInstallation> sdks) : IInventoryService
    {
        public Task<IReadOnlyList<SdkInstallation>> ScanAsync(CancellationToken cancellationToken) =>
            Task.FromResult(sdks);

        public string? GetSystemDotnetPath() => null;
    }

    private sealed class RecordingProcessRunner(ProcessResult result) : IProcessRunner
    {
        public List<ProcessSpec> Invocations { get; } = [];

        public Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken cancellationToken)
        {
            Invocations.Add(spec);
            return Task.FromResult(result);
        }
    }
}
