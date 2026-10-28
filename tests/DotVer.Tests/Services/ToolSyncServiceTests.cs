using DotVer.Application;
using DotVer.Domain;

namespace DotVer.Tests;

public sealed class ToolSyncServiceTests
{
    [Fact]
    public async Task Manifest_AddRemoveRoundTripsThroughToolsJson()
    {
        using var sandbox = new TestSandbox(TestPlatform.Linux);
        var environment = sandbox.CreateEnvironment();
        var paths = DotVerPaths.From(environment);
        var service = new ToolSyncService(new ToolsManifestStore(paths), new FakeProcessRunner(), environment);

        await service.AddAsync("dotnet-ef", "8.0.0", CancellationToken.None);
        await service.AddAsync("dotnet-format", null, CancellationToken.None);
        var manifest = await service.ListAsync(CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(paths.Home, "tools.json")));
        Assert.Equal(["dotnet-ef", "dotnet-format"], manifest.Tools.Select(static tool => tool.Id));
        Assert.Equal("8.0.0", manifest.Tools[0].Version);

        Assert.True(await service.RemoveAsync("dotnet-ef", CancellationToken.None));
        Assert.False(await service.RemoveAsync("dotnet-ef", CancellationToken.None));
        Assert.Equal(["dotnet-format"], (await service.ListAsync(CancellationToken.None)).Tools.Select(static tool => tool.Id));
    }

    [Theory]
    [InlineData("dotnet ef")]
    [InlineData("-not-an-id")]
    [InlineData("tool;rm")]
    public async Task Manifest_RejectsIdsThatAreNotPackageIds(string id)
    {
        using var sandbox = new TestSandbox(TestPlatform.Linux);
        var environment = sandbox.CreateEnvironment();
        var service = new ToolSyncService(new ToolsManifestStore(DotVerPaths.From(environment)), new FakeProcessRunner(), environment);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddAsync(id, null, CancellationToken.None));
    }

    [Fact]
    public async Task Sync_RunsToolUpdateWithTheSelectedSdkHost()
    {
        using var sandbox = new TestSandbox(TestPlatform.Linux);
        var sdk = sandbox.CreateSdkInstall("8.0.412", isManaged: true);
        var environment = sandbox.CreateEnvironment();
        var runner = new FakeProcessRunner();
        var service = new ToolSyncService(new ToolsManifestStore(DotVerPaths.From(environment)), runner, environment);
        await service.AddAsync("dotnet-ef", "8.0.0", CancellationToken.None);

        var result = await service.SyncAsync(sdk, CancellationToken.None);

        var spec = Assert.Single(runner.Invocations);
        Assert.Equal(sdk.DotnetPath, spec.FileName);
        Assert.Equal(["tool", "update", "--global", "dotnet-ef", "--version", "8.0.0"], spec.Arguments);
        Assert.Equal(sdk.HostRoot, spec.EnvironmentVariables?["DOTNET_ROOT"]);
        Assert.Equal("8.0.412", spec.EnvironmentVariables?["DOTVER_VERSION"]);
        var entry = Assert.Single(result.Tools);
        Assert.True(entry.Succeeded);
    }

    [Fact]
    public async Task Sync_FallsBackToInstallWhenUpdateFails()
    {
        using var sandbox = new TestSandbox(TestPlatform.Linux);
        var sdk = sandbox.CreateSdkInstall("8.0.412", isManaged: true);
        var environment = sandbox.CreateEnvironment();
        // Older SDKs refuse `tool update` for a tool that is not installed yet.
        var runner = new FakeProcessRunner(spec => spec.Arguments.Contains("update")
            ? new ProcessResult(1, string.Empty, "Tool 'dotnet-ef' is not currently installed.")
            : new ProcessResult(0, "Tool 'dotnet-ef' was successfully installed.", string.Empty));
        var service = new ToolSyncService(new ToolsManifestStore(DotVerPaths.From(environment)), runner, environment);
        await service.AddAsync("dotnet-ef", null, CancellationToken.None);

        var result = await service.SyncAsync(sdk, CancellationToken.None);

        Assert.Equal(2, runner.Invocations.Count);
        Assert.Equal(["tool", "install", "--global", "dotnet-ef"], runner.Invocations[1].Arguments);
        Assert.True(Assert.Single(result.Tools).Succeeded);
    }

    [Fact]
    public async Task Sync_ReportsFailuresWithoutThrowing()
    {
        using var sandbox = new TestSandbox(TestPlatform.Linux);
        var sdk = sandbox.CreateSdkInstall("8.0.412", isManaged: true);
        var environment = sandbox.CreateEnvironment();
        var runner = new FakeProcessRunner(_ => new ProcessResult(1, string.Empty, "error NU1101: package not found"));
        var service = new ToolSyncService(new ToolsManifestStore(DotVerPaths.From(environment)), runner, environment);
        await service.AddAsync("ghost-tool", null, CancellationToken.None);

        var result = await service.SyncAsync(sdk, CancellationToken.None);

        var entry = Assert.Single(result.Tools);
        Assert.False(entry.Succeeded);
        Assert.Contains("NU1101", entry.Error, StringComparison.Ordinal);
    }
}
