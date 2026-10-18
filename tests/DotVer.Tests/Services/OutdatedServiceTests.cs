using DotVer.Application;
using DotVer.Domain;

namespace DotVer.Tests;

public sealed class OutdatedServiceTests
{
    [Fact]
    public async Task GetOutdated_FlagsUpdateEolAndPrerelease()
    {
        using var sandbox = new TestSandbox(TestPlatform.Windows);
        sandbox.CreateSdkInstall("8.0.400", isManaged: true);
        sandbox.CreateSdkInstall("6.0.428", isManaged: true);
        sandbox.CreateSdkInstall("10.0.100-preview.1", isManaged: true);
        var harness = new Harness(
            sandbox,
            [
                new ReleaseChannel("8.0", "8.0.412", "active", "lts"),
                new ReleaseChannel("6.0", "6.0.428", "eol", "lts"),
                new ReleaseChannel("10.0", "10.0.100-preview.1", "preview", "sts")
            ],
            DotVerConfig.Empty);

        var entries = await harness.Service.GetOutdatedAsync(CancellationToken.None);

        var eight = entries.Single(e => e.Installation.Version.Raw == "8.0.400");
        Assert.True(eight.UpdateAvailable);
        Assert.Equal("8.0.412", eight.LatestInChannel);
        Assert.False(eight.IsEndOfLife);

        var six = entries.Single(e => e.Installation.Version.Raw == "6.0.428");
        Assert.True(six.IsEndOfLife);
        Assert.False(six.UpdateAvailable);

        var ten = entries.Single(e => e.Installation.Version.Raw == "10.0.100-preview.1");
        Assert.True(ten.IsPrerelease);
    }

    [Fact]
    public async Task Upgrade_Single_InstallsLatestRepointsAndPrunes()
    {
        using var sandbox = new TestSandbox(TestPlatform.Windows);
        var old = sandbox.CreateSdkInstall("8.0.400", isManaged: true);
        var config = new DotVerConfig(
            "8.0.400",
            "8.0.400",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["lts"] = "8.0.400" },
            []);
        var harness = new Harness(sandbox, [new ReleaseChannel("8.0", "8.0.412", "active", "lts")], config);
        harness.Install.OnInstall = version => sandbox.CreateSdkInstall(version, isManaged: true);

        var outcomes = await harness.Service.UpgradeAsync("8.0.400", all: false, pruneOld: true, CancellationToken.None);

        var outcome = Assert.Single(outcomes);
        Assert.True(outcome.Upgraded);
        Assert.True(outcome.RepointedCurrent);
        Assert.True(outcome.RepointedDefault);
        Assert.Equal(["lts"], outcome.RepointedAliases);
        Assert.True(outcome.PrunedOld);
        Assert.False(Directory.Exists(old.HostRoot));

        var saved = await harness.ConfigStore.LoadAsync(CancellationToken.None);
        Assert.Equal("8.0.412", saved.CurrentVersion);
        Assert.Equal("8.0.412", saved.DefaultVersion);
        Assert.Equal("8.0.412", saved.Aliases["lts"]);
    }

    [Fact]
    public async Task Upgrade_WhenAlreadyLatest_DoesNotInstall()
    {
        using var sandbox = new TestSandbox(TestPlatform.Windows);
        sandbox.CreateSdkInstall("8.0.412", isManaged: true);
        var harness = new Harness(sandbox, [new ReleaseChannel("8.0", "8.0.412", "active", "lts")], DotVerConfig.Empty);

        var outcomes = await harness.Service.UpgradeAsync("8.0.412", all: false, pruneOld: false, CancellationToken.None);

        Assert.False(Assert.Single(outcomes).Upgraded);
        Assert.Empty(harness.Install.Calls);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("8.0", true)]
    public async Task Upgrade_RequiresExactlyOneOfTokenOrAll(string? token, bool all)
    {
        using var sandbox = new TestSandbox(TestPlatform.Windows);
        var harness = new Harness(sandbox, [], DotVerConfig.Empty);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => harness.Service.UpgradeAsync(token, all, pruneOld: false, CancellationToken.None));
    }

    [Fact]
    public async Task GetOutdated_CrossChannelOrPrereleaseLatest_IsNotAnUpdate()
    {
        using var sandbox = new TestSandbox(TestPlatform.Windows);
        sandbox.CreateSdkInstall("8.0.400", isManaged: true);
        sandbox.CreateSdkInstall("9.0.100", isManaged: true);
        var harness = new Harness(
            sandbox,
            [
                new ReleaseChannel("8.0", "9.0.100", "active", "sts"),
                new ReleaseChannel("9.0", "9.0.200-preview.1", "active", "sts")
            ],
            DotVerConfig.Empty);

        var entries = await harness.Service.GetOutdatedAsync(CancellationToken.None);

        Assert.False(entries.Single(e => e.Channel == "8.0").UpdateAvailable);
        Assert.Null(entries.Single(e => e.Channel == "8.0").LatestInChannel);
        Assert.False(entries.Single(e => e.Channel == "9.0").UpdateAvailable);
        Assert.Equal("9.0.200-preview.1", entries.Single(e => e.Channel == "9.0").LatestInChannel);
    }

    [Fact]
    public async Task Upgrade_AllWithOneFailure_ReportsPerTargetOutcomes()
    {
        using var sandbox = new TestSandbox(TestPlatform.Windows);
        sandbox.CreateSdkInstall("8.0.400", isManaged: true);
        sandbox.CreateSdkInstall("9.0.100", isManaged: true);
        var harness = new Harness(
            sandbox,
            [
                new ReleaseChannel("8.0", "8.0.412", "active", "lts"),
                new ReleaseChannel("9.0", "9.0.203", "active", "sts")
            ],
            DotVerConfig.Empty);
        harness.Install.OnInstall = version => version.StartsWith("9.", StringComparison.Ordinal)
            ? throw new InvalidOperationException("simulated install failure")
            : sandbox.CreateSdkInstall(version, isManaged: true);

        var outcomes = await harness.Service.UpgradeAsync(null, all: true, pruneOld: false, CancellationToken.None);

        Assert.Equal(2, outcomes.Count);
        Assert.Contains(outcomes, o => o.Upgraded && o.Error is null);
        Assert.Contains(outcomes, o => !o.Upgraded && o.Error is not null);
    }

    [Fact]
    public async Task Upgrade_PruneOld_SkipsVersionPinnedByGlobalJson()
    {
        using var sandbox = new TestSandbox(TestPlatform.Windows);
        var old = sandbox.CreateSdkInstall("8.0.400", isManaged: true);
        File.WriteAllText(Path.Combine(sandbox.Root, "global.json"), """{"sdk":{"version":"8.0.400"}}""");
        var harness = new Harness(sandbox, [new ReleaseChannel("8.0", "8.0.412", "active", "lts")], DotVerConfig.Empty);
        harness.Install.OnInstall = version => sandbox.CreateSdkInstall(version, isManaged: true);

        var outcome = Assert.Single(await harness.Service.UpgradeAsync("8.0.400", all: false, pruneOld: true, CancellationToken.None));

        Assert.True(outcome.Upgraded);
        Assert.False(outcome.PrunedOld);
        Assert.Contains("pinned", outcome.Note ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.True(Directory.Exists(old.HostRoot));
    }

    private sealed class Harness
    {
        public Harness(TestSandbox sandbox, IReadOnlyList<ReleaseChannel> channels, DotVerConfig config)
        {
            var environment = sandbox.CreateEnvironment();
            ConfigStore = new InMemoryConfigStore(config);
            var inventory = new InventoryService(DotVerPaths.From(environment), environment, ConfigStore);
            Install = new FakeInstallService(channels);
            var state = new StateService(ConfigStore, new VersionResolver(), inventory, DotVerPaths.From(environment), environment);
            Service = new OutdatedService(inventory, Install, state, new VersionResolver(), new GlobalJsonService(), environment);
        }

        public OutdatedService Service { get; }
        public FakeInstallService Install { get; }
        public InMemoryConfigStore ConfigStore { get; }
    }

    private sealed class FakeInstallService(IReadOnlyList<ReleaseChannel> channels) : IInstallService
    {
        public List<string> Calls { get; } = [];
        public Func<string, SdkInstallation>? OnInstall { get; set; }

        public Task<IReadOnlyList<ReleaseChannel>> ListRemoteAsync(CancellationToken cancellationToken) =>
            Task.FromResult(channels);

        public Task<InstallOutcome> InstallAsync(string versionOrChannel, CancellationToken cancellationToken)
        {
            Calls.Add(versionOrChannel);
            var sdk = OnInstall?.Invoke(versionOrChannel)
                ?? throw new InvalidOperationException("FakeInstallService.OnInstall not configured.");
            return Task.FromResult(new InstallOutcome(
                new InstallPlan(versionOrChannel, sdk.Version.Raw, "test", null),
                sdk,
                WasAlreadyInstalled: false));
        }

        public Task<InstallPlan> ResolveInstallPlanAsync(string token, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
