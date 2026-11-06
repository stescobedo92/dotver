using DotVer.Application;
using DotVer.Domain;

namespace DotVer.Tests;

public sealed class InstallManyTests
{
    private const string Index =
        """{"releases-index":[{"channel-version":"8.0","latest-sdk":"8.0.100","support-phase":"active","release-type":"lts"}]}""";

    [Fact]
    public async Task InstallMany_ContinuesAfterFailuresAndFlagsDuplicates()
    {
        using var sandbox = new TestSandbox(TestPlatform.Linux);
        var environment = sandbox.CreateEnvironment();
        environment.SetUserEnvironmentVariable("DOTVER_NO_CACHE", "1");
        var config = new InMemoryConfigStore(DotVerConfig.Empty);
        var inventory = new InventoryService(DotVerPaths.From(environment), environment, config);
        var process = new FakeProcessRunner(spec =>
        {
            PlantSdkLayout(spec);
            return new ProcessResult(0, string.Empty, string.Empty);
        });
        var service = new InstallService(DotVerPaths.From(environment), environment, new ScriptHttpClient(Index), process, inventory);
        var messages = new List<string>();

        var result = await service.InstallManyAsync(["8.0", "nope", "8.0.100"], options: null, new ListProgress(messages), CancellationToken.None);

        Assert.Equal(3, result.Entries.Count);

        Assert.True(result.Entries[0].Succeeded);
        Assert.Equal("8.0.100", result.Entries[0].Outcome!.Installation.Version.Raw);
        Assert.False(result.Entries[0].Outcome!.WasAlreadyInstalled);

        Assert.False(result.Entries[1].Succeeded);
        Assert.Contains("nope", result.Entries[1].Error, StringComparison.Ordinal);

        Assert.True(result.Entries[2].Duplicate);
        Assert.Null(result.Entries[2].Error);
        Assert.Null(result.Entries[2].Outcome);
        Assert.Equal("8.0.100", result.Entries[2].Plan!.ResolvedVersion);

        Assert.Equal(1, result.Failed);
        Assert.False(result.AllSucceeded);
        Assert.Single(result.Outcomes);
        Assert.Single(process.Invocations);
        Assert.Contains(messages, static message => message.StartsWith("Installing 8.0.100", StringComparison.Ordinal));
    }

    [Fact]
    public async Task InstallMany_WithoutUsableTokens_Throws()
    {
        using var sandbox = new TestSandbox(TestPlatform.Linux);
        var environment = sandbox.CreateEnvironment();
        var config = new InMemoryConfigStore(DotVerConfig.Empty);
        var inventory = new InventoryService(DotVerPaths.From(environment), environment, config);
        var service = new InstallService(DotVerPaths.From(environment), environment, new ScriptHttpClient(Index), new FakeProcessRunner(), inventory);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.InstallManyAsync([" ", ""], options: null, progress: null, CancellationToken.None));
    }

    // Pretends to be dotnet-install.sh: materializes the managed layout at --install-dir.
    private static void PlantSdkLayout(ProcessSpec spec)
    {
        var arguments = spec.Arguments.ToList();
        var directory = arguments[arguments.IndexOf("--install-dir") + 1];
        var version = arguments[arguments.IndexOf("--version") + 1];
        Directory.CreateDirectory(Path.Combine(directory, "sdk", version));
        File.WriteAllText(Path.Combine(directory, "dotnet"), string.Empty);
    }

    private sealed class ListProgress(List<string> sink) : IProgress<string>
    {
        public void Report(string value) => sink.Add(value);
    }

    private sealed class ScriptHttpClient(string index) : IHttpResourceClient
    {
        public Task<string> GetStringAsync(string url, CancellationToken cancellationToken) => Task.FromResult(index);

        public Task DownloadFileAsync(string url, string destinationPath, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.WriteAllText(destinationPath, "# fake dotnet-install script");
            return Task.CompletedTask;
        }

        public Task<long> DownloadToFileAsync(string url, string destinationPath, long maxBytes, Action<long, long?>? onProgress, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Streaming downloads are not used by this test.");
    }
}
