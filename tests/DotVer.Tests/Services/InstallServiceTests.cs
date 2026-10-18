using DotVer.Application;
using DotVer.Domain;

namespace DotVer.Tests;

public sealed class InstallServiceTests
{
    [Fact]
    public async Task Install_FromChannelWithTraversalVersionInRemoteMetadata_FailsClosed()
    {
        using var sandbox = new TestSandbox(TestPlatform.Windows);
        var environment = sandbox.CreateEnvironment();
        var config = new InMemoryConfigStore(DotVerConfig.Empty);
        var inventory = new InventoryService(DotVerPaths.From(environment), environment, config);
        var http = new FakeHttpResourceClient(
            """{"releases-index":[{"channel-version":"8.0","latest-sdk":"8.0.100-a/../../shims","support-phase":"active","release-type":"lts"}]}""");
        var process = new FakeProcessRunner();
        var service = new InstallService(DotVerPaths.From(environment), environment, http, process, inventory);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.InstallAsync("8.0", options: null, CancellationToken.None));

        Assert.Empty(process.Invocations);
        var paths = DotVerPaths.From(environment);
        Assert.False(Directory.Exists(Path.Combine(paths.Home, "shims", "sdk")));
    }

    [Theory]
    [InlineData("x/../dotnet-9.9")]
    [InlineData("-InstallDir")]
    [InlineData("a/b")]
    public async Task InstallRuntime_WithPoisonedChannelInRemoteMetadata_FailsClosed(string poisonedChannel)
    {
        using var sandbox = new TestSandbox(TestPlatform.Windows);
        var environment = sandbox.CreateEnvironment();
        var config = new InMemoryConfigStore(DotVerConfig.Empty);
        var inventory = new InventoryService(DotVerPaths.From(environment), environment, config);
        // The poisoned channel is the ONLY lts entry: without channel-name
        // validation it would be selected and reach the installer. The fake
        // download would throw "Download must not happen", so asserting the
        // failure message is NOT that one proves validation intercepted it.
        var http = new FakeHttpResourceClient(
            "{\"releases-index\":[{\"channel-version\":\"" + poisonedChannel + "\",\"latest-sdk\":\"9.0.100\",\"support-phase\":\"active\",\"release-type\":\"lts\"}]}");
        var process = new FakeProcessRunner();
        var service = new InstallService(DotVerPaths.From(environment), environment, http, process, inventory);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.InstallRuntimeAsync("lts", "dotnet", architecture: null, CancellationToken.None));

        Assert.DoesNotContain("Download must not happen", exception.Message, StringComparison.Ordinal);
        Assert.Empty(process.Invocations);
    }

    private sealed class FakeHttpResourceClient(string payload) : IHttpResourceClient
    {
        public Task<string> GetStringAsync(string url, CancellationToken cancellationToken) =>
            Task.FromResult(payload);

        public Task DownloadFileAsync(string url, string destinationPath, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Download must not happen for invalid remote metadata.");
    }
}

