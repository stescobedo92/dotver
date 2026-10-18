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
            () => service.InstallAsync("8.0", CancellationToken.None));

        Assert.Empty(process.Invocations);
        var paths = DotVerPaths.From(environment);
        Assert.False(Directory.Exists(Path.Combine(paths.Home, "shims", "sdk")));
    }

    private sealed class FakeHttpResourceClient(string payload) : IHttpResourceClient
    {
        public Task<string> GetStringAsync(string url, CancellationToken cancellationToken) =>
            Task.FromResult(payload);

        public Task DownloadFileAsync(string url, string destinationPath, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Download must not happen for invalid remote metadata.");
    }
}
