using DotVer.Application;
using DotVer.Domain;

namespace DotVer.Tests;

public sealed class EnsureServiceTests
{
    [Fact]
    public async Task Ensure_WhenPinAlreadySatisfied_DoesNotInstall()
    {
        using var sandbox = new TestSandbox(TestPlatform.Windows);
        WritePin(sandbox, "8.0.412", rollForward: null);
        var sdk = sandbox.CreateSdkInstall("8.0.412", isManaged: true);
        var harness = new Harness(sandbox, [sdk]);

        var outcome = await harness.Service.EnsureAsync(setCurrent: false, directory: null, CancellationToken.None);

        Assert.False(outcome.InstalledNow);
        Assert.Equal("8.0.412", outcome.Installation.Version.Raw);
        Assert.Empty(harness.InstallService.Calls);
    }

    [Fact]
    public async Task Ensure_WhenPinMissing_InstallsExactVersion()
    {
        using var sandbox = new TestSandbox(TestPlatform.Windows);
        WritePin(sandbox, "8.0.412", rollForward: null);
        var installed = sandbox.CreateSdkInstall("8.0.412", isManaged: true);
        var harness = new Harness(sandbox, []);
        harness.InstallService.OnInstall = version =>
        {
            harness.Inventory.Add(installed);
            return installed;
        };

        var outcome = await harness.Service.EnsureAsync(setCurrent: false, directory: null, CancellationToken.None);

        Assert.True(outcome.InstalledNow);
        Assert.Equal(["8.0.412"], harness.InstallService.Calls);
        Assert.Equal("8.0.412", outcome.Installation.Version.Raw);
    }

    [Fact]
    public async Task Ensure_WithoutPin_Throws()
    {
        using var sandbox = new TestSandbox(TestPlatform.Windows);
        File.WriteAllText(Path.Combine(sandbox.Root, "global.json"), "{}");
        var harness = new Harness(sandbox, []);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => harness.Service.EnsureAsync(setCurrent: false, directory: null, CancellationToken.None));
    }

    [Fact]
    public async Task Ensure_WithUse_SetsCurrentOnly()
    {
        using var sandbox = new TestSandbox(TestPlatform.Windows);
        WritePin(sandbox, "8.0.412", rollForward: null);
        var sdk = sandbox.CreateSdkInstall("8.0.412", isManaged: true);
        var harness = new Harness(sandbox, [sdk]);

        await harness.Service.EnsureAsync(setCurrent: true, directory: null, CancellationToken.None);

        var config = await harness.ConfigStore.LoadAsync(CancellationToken.None);
        Assert.Equal("8.0.412", config.CurrentVersion);
        Assert.Null(config.DefaultVersion);
    }

    [Fact]
    public async Task Ensure_RollForwardFeature_MatchesHigherBandWithoutInstalling()
    {
        using var sandbox = new TestSandbox(TestPlatform.Windows);
        WritePin(sandbox, "8.0.200", rollForward: "feature");
        var sdk = sandbox.CreateSdkInstall("8.0.412", isManaged: true);
        var harness = new Harness(sandbox, [sdk]);

        var outcome = await harness.Service.EnsureAsync(setCurrent: false, directory: null, CancellationToken.None);

        Assert.False(outcome.InstalledNow);
        Assert.Equal("8.0.412", outcome.Installation.Version.Raw);
    }

    [Fact]
    public async Task Ensure_PatchRollForwardBelowPin_DoesNotCountAsSatisfied()
    {
        using var sandbox = new TestSandbox(TestPlatform.Windows);
        WritePin(sandbox, "8.0.412", rollForward: "patch");
        var older = sandbox.CreateSdkInstall("8.0.400", isManaged: true);
        var exact = sandbox.CreateSdkInstall("8.0.412", isManaged: true);
        var harness = new Harness(sandbox, [older]);
        harness.InstallService.OnInstall = version =>
        {
            harness.Inventory.Add(exact);
            return exact;
        };

        var outcome = await harness.Service.EnsureAsync(setCurrent: false, directory: null, CancellationToken.None);

        Assert.True(outcome.InstalledNow);
        Assert.Equal("8.0.412", outcome.Installation.Version.Raw);
    }

    private static void WritePin(TestSandbox sandbox, string version, string? rollForward)
    {
        var rollForwardLine = rollForward is null ? string.Empty : $", \"rollForward\": \"{rollForward}\"";
        File.WriteAllText(
            Path.Combine(sandbox.Root, "global.json"),
            $"{{\"sdk\":{{\"version\":\"{version}\"{rollForwardLine}}}}}");
    }

    private sealed class Harness
    {
        public Harness(TestSandbox sandbox, IReadOnlyList<SdkInstallation> initial)
        {
            var environment = sandbox.CreateEnvironment();
            Inventory = new FakeInventoryService([.. initial]);
            InstallService = new FakeInstallService();
            ConfigStore = new InMemoryConfigStore(DotVerConfig.Empty);
            var state = new StateService(ConfigStore, new VersionResolver(), Inventory, DotVerPaths.From(environment), environment);
            Service = new EnsureService(new GlobalJsonService(), Inventory, InstallService, state, environment);
        }

        public EnsureService Service { get; }
        public FakeInventoryService Inventory { get; }
        public FakeInstallService InstallService { get; }
        public InMemoryConfigStore ConfigStore { get; }
    }

    private sealed class FakeInventoryService(List<SdkInstallation> installations) : IInventoryService
    {
        public Task<IReadOnlyList<SdkInstallation>> ScanAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SdkInstallation>>([.. installations]);

        public string? GetSystemDotnetPath() => null;

        public void Add(SdkInstallation sdk) => installations.Add(sdk);
    }

    private sealed class FakeInstallService : IInstallService
    {
        public List<string> Calls { get; } = [];
        public Func<string, SdkInstallation>? OnInstall { get; set; }

        public Task<InstallOutcome> InstallAsync(string versionOrChannel, CancellationToken cancellationToken)
        {
            Calls.Add(versionOrChannel);
            var sdk = OnInstall?.Invoke(versionOrChannel)
                ?? throw new InvalidOperationException("FakeInstallService.OnInstall not configured.");
            var plan = new InstallPlan(versionOrChannel, sdk.Version.Raw, "test", null);
            return Task.FromResult(new InstallOutcome(plan, sdk, WasAlreadyInstalled: false));
        }

        public Task<InstallPlan> ResolveInstallPlanAsync(string token, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ReleaseChannel>> ListRemoteAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}

