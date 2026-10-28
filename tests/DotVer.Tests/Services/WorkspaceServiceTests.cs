using DotVer.Application;
using DotVer.Domain;

namespace DotVer.Tests;

public sealed class WorkspaceServiceTests
{
    [Fact]
    public async Task Scan_FindsNestedPinsSkipsBuildDirsAndMatchesInventory()
    {
        using var sandbox = new TestSandbox(TestPlatform.Windows);
        var root = CreateWorkspace(sandbox);
        var sdk = sandbox.CreateSdkInstall("8.0.412", isManaged: true);
        var service = new WorkspaceService(new GlobalJsonService(), new StubInventory([sdk]), new FakeInstall());

        var scan = await service.ScanAsync(root, CancellationToken.None);

        Assert.False(scan.Truncated);
        Assert.Equal(3, scan.Entries.Count);
        var satisfied = scan.Entries.Single(e => e.RequestedVersion == "8.0.400");
        Assert.True(satisfied.Satisfied);
        Assert.Equal("8.0.412", satisfied.MatchedVersion);
        Assert.False(scan.Entries.Single(e => e.RequestedVersion == "9.0.100").Satisfied);
        Assert.Contains(scan.Entries, e => e.RequestedVersion is null);
    }

    [Fact]
    public async Task EnsureRecursive_InstallsDistinctMissingVersionsOnce()
    {
        using var sandbox = new TestSandbox(TestPlatform.Windows);
        var root = CreateWorkspace(sandbox);
        var sdk8 = sandbox.CreateSdkInstall("8.0.412", isManaged: true);
        var inventory = new MutableInventory([sdk8]);
        var install = new FakeInstall();
        var sdk9 = sandbox.CreateSdkInstall("9.0.100", isManaged: true);
        install.OnInstall = _ =>
        {
            inventory.Add(sdk9);
            return sdk9;
        };
        var service = new WorkspaceService(new GlobalJsonService(), inventory, install);

        var result = await service.EnsureRecursiveAsync(root, CancellationToken.None);

        Assert.Equal(["9.0.100"], install.Calls);
        Assert.Equal(["9.0.100"], result.InstalledVersions);
        Assert.Empty(result.Errors);
        Assert.All(
            result.Scan.Entries.Where(e => e.RequestedVersion is not null),
            static e => Assert.True(e.Satisfied));
    }

    [Fact]
    public async Task PlanEnsure_ListsDistinctMissingVersionsWithoutInstalling()
    {
        using var sandbox = new TestSandbox(TestPlatform.Windows);
        var root = CreateWorkspace(sandbox);
        var sdk8 = sandbox.CreateSdkInstall("8.0.412", isManaged: true);
        var install = new FakeInstall();
        var service = new WorkspaceService(new GlobalJsonService(), new StubInventory([sdk8]), install);

        var plan = await service.PlanEnsureAsync(root, CancellationToken.None);

        // `scan --ensure --dry-run`: report what would be installed, touch nothing.
        Assert.Equal(["9.0.100"], plan.MissingVersions);
        Assert.Empty(plan.Errors);
        Assert.Empty(install.Calls);
        Assert.Equal(3, plan.Scan.Entries.Count);
    }

    [Fact]
    public async Task Scan_MalformedPin_IsInvalidNotSatisfied()
    {
        using var sandbox = new TestSandbox(TestPlatform.Windows);
        var root = Path.Combine(sandbox.Root, "workspace2");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "global.json"), "{ broken json");
        var service = new WorkspaceService(new GlobalJsonService(), new StubInventory([]), new FakeInstall());

        var scan = await service.ScanAsync(root, CancellationToken.None);

        var entry = Assert.Single(scan.Entries);
        Assert.True(entry.Invalid);
        Assert.False(entry.Satisfied);
    }

    [Fact]
    public async Task Scan_LinkedGlobalJson_IsInvalidNotFollowed()
    {
        using var sandbox = new TestSandbox(TestPlatform.Windows);
        var root = Path.Combine(sandbox.Root, "workspace3");
        var outside = Path.Combine(sandbox.Root, "outside.json");
        Directory.CreateDirectory(root);
        File.WriteAllText(outside, """{"sdk":{"version":"8.0.412"}}""");
        try
        {
            File.CreateSymbolicLink(Path.Combine(root, "global.json"), outside);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Symlink creation needs a privilege this host may not grant;
            // Linux/macOS CI exercises the assertion below.
            return;
        }

        var service = new WorkspaceService(new GlobalJsonService(), new StubInventory([]), new FakeInstall());
        var scan = await service.ScanAsync(root, CancellationToken.None);

        var entry = Assert.Single(scan.Entries);
        Assert.True(entry.Invalid);
        Assert.Null(entry.RequestedVersion);
    }

    [Fact]
    public async Task Scan_MissingRoot_Throws()
    {
        var service = new WorkspaceService(new GlobalJsonService(), new StubInventory([]), new FakeInstall());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ScanAsync(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), CancellationToken.None));
    }

    private static string CreateWorkspace(TestSandbox sandbox)
    {
        var root = Path.Combine(sandbox.Root, "workspace");
        var app = Path.Combine(root, "src", "app");
        var lib = Path.Combine(root, "src", "lib");
        var skipped = Path.Combine(root, "src", "app", "bin");
        var empty = Path.Combine(root, "tools");
        Directory.CreateDirectory(app);
        Directory.CreateDirectory(lib);
        Directory.CreateDirectory(skipped);
        Directory.CreateDirectory(empty);

        File.WriteAllText(Path.Combine(app, "global.json"), """{"sdk":{"version":"8.0.400","rollForward":"feature"}}""");
        File.WriteAllText(Path.Combine(lib, "global.json"), """{"sdk":{"version":"9.0.100"}}""");
        File.WriteAllText(Path.Combine(skipped, "global.json"), """{"sdk":{"version":"6.0.100"}}""");
        File.WriteAllText(Path.Combine(empty, "global.json"), """{"tools":{}}""");
        return root;
    }

    private sealed class StubInventory(IReadOnlyList<SdkInstallation> sdks) : IInventoryService
    {
        public Task<IReadOnlyList<SdkInstallation>> ScanAsync(CancellationToken cancellationToken) =>
            Task.FromResult(sdks);

        public Task<IReadOnlyList<SdkInstallation>> ScanAsync(bool forceRefresh, CancellationToken cancellationToken) =>
            ScanAsync(cancellationToken);

        public string? GetSystemDotnetPath() => null;
    }

    private sealed class MutableInventory(List<SdkInstallation> sdks) : IInventoryService
    {
        public Task<IReadOnlyList<SdkInstallation>> ScanAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SdkInstallation>>([.. sdks]);

        public Task<IReadOnlyList<SdkInstallation>> ScanAsync(bool forceRefresh, CancellationToken cancellationToken) =>
            ScanAsync(cancellationToken);

        public string? GetSystemDotnetPath() => null;

        public void Add(SdkInstallation sdk) => sdks.Add(sdk);
    }

    private sealed class FakeInstall : IInstallService
    {
        public List<string> Calls { get; } = [];
        public Func<string, SdkInstallation>? OnInstall { get; set; }

        public Task<InstallOutcome> InstallAsync(string versionOrChannel, InstallOptions? options, CancellationToken cancellationToken)
        {
            Calls.Add(versionOrChannel);
            var sdk = OnInstall?.Invoke(versionOrChannel)
                ?? throw new InvalidOperationException("OnInstall not configured.");
            return Task.FromResult(new InstallOutcome(
                new InstallPlan(versionOrChannel, sdk.Version.Raw, "test", null), sdk, WasAlreadyInstalled: false));
        }

        public Task<InstallOutcome> InstallFromArchiveAsync(string version, string archivePath, ArchiveChecksum checksum, InstallOptions? options, CancellationToken cancellationToken) =>

            throw new NotSupportedException();


        public Task<RuntimeInstallOutcome> InstallRuntimeAsync(string versionOrChannel, string runtime, string? architecture, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<InstallPlan> ResolveInstallPlanAsync(string token, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ReleaseChannel>> ListRemoteAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}


