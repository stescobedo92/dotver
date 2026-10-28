using System.Text.Json;
using DotVer.Application;
using DotVer.Domain;

namespace DotVer.Tests;

public sealed class InfoAndArchTests
{
    [Fact]
    public async Task InfoReportsRuntimesWorkloadsAndArchitectureFromDisk()
    {
        using var sandbox = new TestSandbox(TestPlatform.Linux);
        var root = Path.Combine(sandbox.Root, ".dotver", "installs", "8.0.404--arm64");
        Directory.CreateDirectory(Path.Combine(root, "sdk", "8.0.404"));
        File.WriteAllText(Path.Combine(root, "dotnet"), string.Empty);
        Directory.CreateDirectory(Path.Combine(root, "shared", "Microsoft.NETCore.App", "8.0.10"));
        Directory.CreateDirectory(Path.Combine(root, "shared", "Microsoft.AspNetCore.App", "8.0.10"));
        Directory.CreateDirectory(Path.Combine(root, "sdk-manifests", "8.0.400", "microsoft.net.workload.mono.toolchain"));

        var environment = sandbox.CreateEnvironment();
        var paths = DotVerPaths.From(environment);
        var inventory = new InventoryService(paths, environment, new FakeConfigStore());
        var installations = await inventory.ScanAsync(CancellationToken.None);
        Assert.Equal("arm64", Assert.Single(installations).Architecture);

        var exec = new FakeExec(Assert.Single(installations));
        var info = await new InfoService(exec, new FakeSelection()).GetInfoAsync("8.0.404", CancellationToken.None);

        Assert.Equal("arm64", info.Installation.Architecture);
        Assert.Equal(2, info.Runtimes.Count);
        Assert.Contains("microsoft.net.workload.mono.toolchain", info.Workloads);

        using var document = JsonDocument.Parse(JsonReports.ForInfo(info));
        Assert.Equal("arm64", document.RootElement.GetProperty("architecture").GetString());
    }

    [Theory]
    [InlineData("8.0.404--wasm", "8.0.404", "unknown")]
    [InlineData("9.0.100-rc--x64--arm64", "9.0.100-rc--x64", "arm64")]
    public async Task InventoryArchitectureParsingIsFailClosed(string leaf, string version, string expectedArchitecture)
    {
        using var sandbox = new TestSandbox(TestPlatform.Linux);
        var root = Path.Combine(sandbox.Root, ".dotver", "installs", leaf);
        Directory.CreateDirectory(Path.Combine(root, "sdk", version));
        File.WriteAllText(Path.Combine(root, "dotnet"), string.Empty);

        var environment = sandbox.CreateEnvironment();
        var inventory = new InventoryService(DotVerPaths.From(environment), environment, new FakeConfigStore());
        var installations = await inventory.ScanAsync(CancellationToken.None);

        var sdk = Assert.Single(installations);
        Assert.Equal(expectedArchitecture, sdk.Architecture);
        Assert.False(Architectures.MatchesHost(sdk.Architecture, "x64"));
    }

    [Fact]
    public void AutomaticMatchingNeverSelectsForeignArchitecture()
    {
        var foreign = new SdkInstallation(
            SdkVersion.Parse("9.0.100"), "/root/arm", "/root/arm/sdk/9.0.100", "/root/arm/dotnet",
            SdkOriginKind.ManagedInstall, "test", true, true, "test", "arm64");
        var native = new SdkInstallation(
            SdkVersion.Parse("8.0.412"), "/root/x64", "/root/x64/sdk/8.0.412", "/root/x64/dotnet",
            SdkOriginKind.ManagedInstall, "test", true, true, "test", null);
        var inventory = new List<SdkInstallation> { foreign, native };

        // Pin exact to the foreign-only version: no automatic match.
        var exactPin = new GlobalJsonRequest(SdkVersion.Parse("9.0.100"), SdkRollForward.Disable, "/p/global.json");
        Assert.Null(GlobalJsonMatcher.Match(exactPin, inventory, "x64"));

        // LatestMajor roll-forward must prefer the native, not the higher foreign.
        var rollPin = new GlobalJsonRequest(SdkVersion.Parse("8.0.412"), SdkRollForward.LatestMajor, "/p/global.json");
        Assert.Equal(native, GlobalJsonMatcher.Match(rollPin, inventory, "x64"));

        // "latest" with a host-aware resolver ignores the higher foreign version.
        using var sandbox = new TestSandbox(TestPlatform.Linux);
        var resolver = new VersionResolver(sandbox.CreateEnvironment());
        var found = resolver.FindByToken("latest", DotVerConfig.Empty, inventory);
        Assert.Equal(native, found);

        // Without host filtering (prune protection) the foreign still matches.
        Assert.Equal(foreign, GlobalJsonMatcher.Match(exactPin, inventory));
    }

    [Fact]
    public void PrefixResolutionPrefersHostBuildOverHigherForeign()
    {
        var foreign = new SdkInstallation(
            SdkVersion.Parse("8.0.404"), "/root/arm", "/root/arm/sdk/8.0.404", "/root/arm/dotnet",
            SdkOriginKind.ManagedInstall, "test", true, true, "test", "arm64");
        var native = new SdkInstallation(
            SdkVersion.Parse("8.0.100"), "/root/x64", "/root/x64/sdk/8.0.100", "/root/x64/dotnet",
            SdkOriginKind.ManagedInstall, "test", true, true, "test", null);

        using var sandbox = new TestSandbox(TestPlatform.Linux);
        var resolver = new VersionResolver(sandbox.CreateEnvironment());

        Assert.Equal(native, resolver.FindByToken("8.0", DotVerConfig.Empty, [foreign, native]));
    }

    [Fact]
    public async Task PruneKeepsLatestPerArchitectureAndDeletesByIdentity()
    {
        using var sandbox = new TestSandbox(TestPlatform.Linux);
        sandbox.CreateSdkInstall("8.0.412", isManaged: true);
        var foreignRoot = Path.Combine(sandbox.Root, ".dotver", "installs", "8.0.500--arm64");
        Directory.CreateDirectory(Path.Combine(foreignRoot, "sdk", "8.0.500"));
        File.WriteAllText(Path.Combine(foreignRoot, "dotnet"), string.Empty);

        var environment = sandbox.CreateEnvironment();
        var paths = DotVerPaths.From(environment);
        var configStore = new InMemoryConfigStore(DotVerConfig.Empty);
        var inventory = new InventoryService(paths, environment, configStore);
        var state = new StateService(configStore, new VersionResolver(environment), inventory, paths, environment);
        var prune = new PruneService(inventory, state, new GlobalJsonService(), environment);

        var result = await prune.PruneAsync(keepLatestPerChannel: true, execute: true, CancellationToken.None);

        // Each architecture keeps its own latest: nothing is removed.
        Assert.All(result.Entries, entry => Assert.Equal(PruneAction.Keep, entry.Action));
        Assert.True(Directory.Exists(Path.Combine(paths.ManagedInstalls, "8.0.412")));
        Assert.True(Directory.Exists(foreignRoot));
    }

    [Fact]
    public async Task PruneProtectsHostBuildResolvedByRollForwardPin()
    {
        using var sandbox = new TestSandbox(TestPlatform.Linux);
        var pinned = sandbox.CreateSdkInstall("7.0.115", isManaged: true);
        sandbox.CreateSdkInstall("7.0.410", isManaged: true);
        var foreignRoot = Path.Combine(sandbox.Root, ".dotver", "installs", "7.0.116--arm64");
        Directory.CreateDirectory(Path.Combine(foreignRoot, "sdk", "7.0.116"));
        File.WriteAllText(Path.Combine(foreignRoot, "dotnet"), string.Empty);
        File.WriteAllText(
            Path.Combine(sandbox.Root, "global.json"),
            """{"sdk":{"version":"7.0.100","rollForward":"patch"}}""");

        var environment = sandbox.CreateEnvironment();
        var paths = DotVerPaths.From(environment);
        var configStore = new InMemoryConfigStore(DotVerConfig.Empty);
        var inventory = new InventoryService(paths, environment, configStore);
        var state = new StateService(configStore, new VersionResolver(environment), inventory, paths, environment);
        var prune = new PruneService(inventory, state, new GlobalJsonService(), environment);

        var result = await prune.PruneAsync(keepLatestPerChannel: true, execute: true, CancellationToken.None);

        // The pin (host-aware) resolves to 7.0.115: it must survive prune even
        // though a higher foreign build exists in the same feature band.
        Assert.True(Directory.Exists(pinned.HostRoot));
        Assert.Equal(PruneAction.Skipped, result.Entries.Single(e => e.Version == "7.0.115").Action);
    }

    [Fact]
    public void ArchitecturesRejectUnknownAndNormalizeCase()
    {
        Assert.Equal("x64", Architectures.Normalize(null, "x64"));
        Assert.Equal("arm64", Architectures.Normalize("ARM64", "x64"));
        Assert.Throws<InvalidOperationException>(() => Architectures.Normalize("../../evil", "x64"));
        Assert.Throws<InvalidOperationException>(() => Architectures.Normalize("x64; rm -rf /", "x64"));
    }

    private sealed class FakeConfigStore : IConfigStore
    {
        public Task<DotVerConfig> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new DotVerConfig(null, null, new Dictionary<string, string>(), []));

        public Task SaveAsync(DotVerConfig config, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeSelection : ISelectionService
    {
        public Task<SdkSelection> ResolveAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<SelectionTrace> ExplainAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakeExec(SdkInstallation sdk) : IExecService
    {
        public Task<SdkInstallation> ResolveSdkAsync(string token, CancellationToken cancellationToken) =>
            Task.FromResult(sdk);

        public Task<ProcessResult> ExecuteAsync(
            string token,
            IReadOnlyList<string> command,
            bool captureOutput,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<string> WhichAsync(string token, CancellationToken cancellationToken) =>
            Task.FromResult(sdk.DotnetPath);
    }
}
