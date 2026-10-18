using DotVer.Application;
using DotVer.Domain;

namespace DotVer.Tests;

public sealed class DoctorPruneServiceTests
{
    [Fact]
    public async Task Diagnose_FlagsMissingStateAndFixSkipsRepairWhenRootMissing()
    {
        using var sandbox = new TestSandbox(TestPlatform.Windows);
        sandbox.CreateSdkInstall("8.0.412", isManaged: true);
        var environment = sandbox.CreateEnvironment();
        var paths = DotVerPaths.From(environment);

        // Incomplete managed directory: no host, no sdk/<version>.
        Directory.CreateDirectory(Path.Combine(paths.ManagedInstalls, "7.0.100"));
        // Shim present so only the state issues below are fixable noise.
        Directory.CreateDirectory(paths.Shims);
        File.WriteAllText(Path.Combine(paths.Shims, "dotnet.cmd"), string.Empty);

        var missingRoot = Path.Combine(sandbox.Root, "no-such-root");
        var configStore = new InMemoryConfigStore(new DotVerConfig(
            "6.0.999",
            "8.0.412",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["gone"] = "6.0.999",
                ["lts"] = "8.0.412"
            },
            [missingRoot]));
        var inventory = new InventoryService(paths, environment, configStore);
        var bootstrap = new FakeBootstrap(paths);
        var doctor = new DoctorService(inventory, configStore, new GlobalJsonService(), bootstrap, paths, environment);

        var diagnosis = await doctor.DiagnoseAsync(CancellationToken.None);
        var codes = diagnosis.Issues.Select(static issue => issue.Code).ToList();
        Assert.Contains("current-missing", codes);
        Assert.Contains("alias-missing", codes);
        Assert.Contains("root-missing", codes);
        Assert.Contains("incomplete-managed", codes);
        Assert.DoesNotContain("default-missing", codes);

        // A missing registered root means the scan may be degraded (disconnected
        // drive): fix must refuse to destroy config based on negative evidence.
        var fixedResult = await doctor.FixAsync(CancellationToken.None);

        Assert.Equal(1, bootstrap.Calls);
        var remainingCodes = fixedResult.Issues.Select(static issue => issue.Code).ToList();
        Assert.Contains("repair-skipped", remainingCodes);
        Assert.Contains("incomplete-managed", remainingCodes);

        var saved = await configStore.LoadAsync(CancellationToken.None);
        Assert.Equal("6.0.999", saved.CurrentVersion);
        Assert.True(saved.Aliases.ContainsKey("gone"));
        Assert.Equal([missingRoot], saved.RegisteredRoots);
    }

    [Fact]
    public async Task Fix_WithoutMissingRoots_RepairsDanglingState()
    {
        using var sandbox = new TestSandbox(TestPlatform.Windows);
        sandbox.CreateSdkInstall("8.0.412", isManaged: true);
        var environment = sandbox.CreateEnvironment();
        var paths = DotVerPaths.From(environment);
        Directory.CreateDirectory(paths.Shims);
        File.WriteAllText(Path.Combine(paths.Shims, "dotnet.cmd"), string.Empty);

        var configStore = new InMemoryConfigStore(new DotVerConfig(
            "6.0.999",
            "8.0.412",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["gone"] = "6.0.999",
                ["lts"] = "8.0.412"
            },
            []));
        var inventory = new InventoryService(paths, environment, configStore);
        var doctor = new DoctorService(inventory, configStore, new GlobalJsonService(), new FakeBootstrap(paths), paths, environment);

        var result = await doctor.FixAsync(CancellationToken.None);

        Assert.NotEmpty(result.FixesApplied);
        var saved = await configStore.LoadAsync(CancellationToken.None);
        Assert.Null(saved.CurrentVersion);
        Assert.Equal("8.0.412", saved.DefaultVersion);
        Assert.False(saved.Aliases.ContainsKey("gone"));
        Assert.Equal("8.0.412", saved.Aliases["lts"]);
    }

    [Fact]
    public async Task Fix_SanitizesControlCharactersFromConfig()
    {
        using var sandbox = new TestSandbox(TestPlatform.Windows);
        sandbox.CreateSdkInstall("8.0.412", isManaged: true);
        var environment = sandbox.CreateEnvironment();
        var paths = DotVerPaths.From(environment);
        Directory.CreateDirectory(paths.Shims);
        File.WriteAllText(Path.Combine(paths.Shims, "dotnet.cmd"), string.Empty);

        var configStore = new InMemoryConfigStore(new DotVerConfig(
            "6.0.999[31mEVIL",
            "8.0.412",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["gone[31m"] = "6.0.999"
            },
            []));
        var inventory = new InventoryService(paths, environment, configStore);
        var doctor = new DoctorService(inventory, configStore, new GlobalJsonService(), new FakeBootstrap(paths), paths, environment);

        var result = await doctor.FixAsync(CancellationToken.None);

        Assert.NotEmpty(result.FixesApplied);
        Assert.All(result.FixesApplied, fix => Assert.DoesNotContain((char)27, fix));
    }

    [Fact]
    public async Task Prune_ProtectsSdkMatchedByRollForwardPin()
    {
        using var sandbox = new TestSandbox(TestPlatform.Windows);
        var matched = sandbox.CreateSdkInstall("7.0.115", isManaged: true);
        var latest = sandbox.CreateSdkInstall("7.0.410", isManaged: true);
        File.WriteAllText(
            Path.Combine(sandbox.Root, "global.json"),
            """{"sdk":{"version":"7.0.100","rollForward":"patch"}}""");

        var environment = sandbox.CreateEnvironment();
        var paths = DotVerPaths.From(environment);
        var configStore = new InMemoryConfigStore(DotVerConfig.Empty);
        var inventory = new InventoryService(paths, environment, configStore);
        var state = new StateService(configStore, new VersionResolver(), inventory, paths, environment);
        var prune = new PruneService(inventory, state, new GlobalJsonService(), environment);

        var result = await prune.PruneAsync(keepLatestPerChannel: true, execute: true, CancellationToken.None);

        Assert.Equal(PruneAction.Skipped, result.Entries.Single(e => e.Version == "7.0.115").Action);
        Assert.Equal(PruneAction.Keep, result.Entries.Single(e => e.Version == "7.0.410").Action);
        Assert.True(Directory.Exists(matched.HostRoot));
        Assert.True(Directory.Exists(latest.HostRoot));
    }

    [Fact]
    public async Task Prune_DryRunProtectsCurrentAndExecuteRemovesOldPatches()
    {
        using var sandbox = new TestSandbox(TestPlatform.Windows);
        var old = sandbox.CreateSdkInstall("8.0.400", isManaged: true);
        var latest8 = sandbox.CreateSdkInstall("8.0.412", isManaged: true);
        sandbox.CreateSdkInstall("9.0.100", isManaged: true);
        WritePayload(old.HostRoot, 2048);
        WritePayload(latest8.HostRoot, 1024);

        var environment = sandbox.CreateEnvironment();
        var paths = DotVerPaths.From(environment);
        var configStore = new InMemoryConfigStore(new DotVerConfig(
            "8.0.400",
            null,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            []));
        var inventory = new InventoryService(paths, environment, configStore);
        var state = new StateService(configStore, new VersionResolver(), inventory, paths, environment);
        var prune = new PruneService(inventory, state, new GlobalJsonService(), environment);

        var dryRun = await prune.PruneAsync(keepLatestPerChannel: true, execute: false, CancellationToken.None);
        Assert.True(dryRun.DryRun);
        Assert.True(dryRun.TotalBytes >= 3072);
        var oldEntry = dryRun.Entries.Single(e => e.Version == "8.0.400");
        Assert.Equal(PruneAction.Skipped, oldEntry.Action);
        Assert.Equal("current version", oldEntry.Reason);
        Assert.True(Directory.Exists(old.HostRoot));

        await state.ClearCurrentAsync(CancellationToken.None);
        var executed = await prune.PruneAsync(keepLatestPerChannel: true, execute: true, CancellationToken.None);
        Assert.False(executed.DryRun);
        Assert.Equal(PruneAction.Removed, executed.Entries.Single(e => e.Version == "8.0.400").Action);
        Assert.Equal(PruneAction.Keep, executed.Entries.Single(e => e.Version == "8.0.412").Action);
        Assert.False(Directory.Exists(old.HostRoot));
        Assert.True(Directory.Exists(latest8.HostRoot));
    }

    [Fact]
    public async Task Prune_WithoutKeepFlag_OnlyReportsUsage()
    {
        using var sandbox = new TestSandbox(TestPlatform.Windows);
        var old = sandbox.CreateSdkInstall("8.0.400", isManaged: true);
        sandbox.CreateSdkInstall("8.0.412", isManaged: true);
        var environment = sandbox.CreateEnvironment();
        var paths = DotVerPaths.From(environment);
        var configStore = new InMemoryConfigStore(DotVerConfig.Empty);
        var inventory = new InventoryService(paths, environment, configStore);
        var state = new StateService(configStore, new VersionResolver(), inventory, paths, environment);
        var prune = new PruneService(inventory, state, new GlobalJsonService(), environment);

        var result = await prune.PruneAsync(keepLatestPerChannel: false, execute: true, CancellationToken.None);

        Assert.All(result.Entries, static entry => Assert.Equal(PruneAction.Keep, entry.Action));
        Assert.Equal(0, result.ReclaimableBytes);
        Assert.True(Directory.Exists(old.HostRoot));
    }

    private static void WritePayload(string root, int bytes) =>
        File.WriteAllBytes(Path.Combine(root, "payload.bin"), new byte[bytes]);

    private sealed class FakeBootstrap(DotVerPaths paths) : IEnvironmentBootstrapService
    {
        public int Calls { get; private set; }

        public Task<EnvironmentBootstrapResult> EnsureConfiguredAsync(CancellationToken cancellationToken)
        {
            Calls++;
            Directory.CreateDirectory(paths.Shims);
            File.WriteAllText(Path.Combine(paths.Shims, "dotnet.cmd"), string.Empty);
            return Task.FromResult(new EnvironmentBootstrapResult(paths.Shims, false, false, [], []));
        }
    }
}

