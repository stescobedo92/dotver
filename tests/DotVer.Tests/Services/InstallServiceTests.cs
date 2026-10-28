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

    [Theory]
    [InlineData("not json at all")]
    [InlineData("""{"nope":[]}""")]
    [InlineData("""{"releases-index":"string-instead-of-array"}""")]
    public async Task ListRemote_OnMalformedIndex_ThrowsACleanError(string payload)
    {
        using var sandbox = new TestSandbox(TestPlatform.Windows);
        var environment = sandbox.CreateEnvironment();
        var config = new InMemoryConfigStore(DotVerConfig.Empty);
        var inventory = new InventoryService(DotVerPaths.From(environment), environment, config);
        var service = new InstallService(DotVerPaths.From(environment), environment, new FakeHttpResourceClient(payload), new FakeProcessRunner(), inventory);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ListRemoteAsync(CancellationToken.None));

        Assert.Contains("Release metadata", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListRemote_SkipsEntriesMissingRequiredFields()
    {
        using var sandbox = new TestSandbox(TestPlatform.Windows);
        var environment = sandbox.CreateEnvironment();
        var config = new InMemoryConfigStore(DotVerConfig.Empty);
        var inventory = new InventoryService(DotVerPaths.From(environment), environment, config);
        var http = new FakeHttpResourceClient(
            """{"releases-index":[{"channel-version":"8.0"},"garbage",{"channel-version":"9.0","latest-sdk":"9.0.100","support-phase":"active","release-type":"sts"}]}""");
        var service = new InstallService(DotVerPaths.From(environment), environment, http, new FakeProcessRunner(), inventory);

        var channels = await service.ListRemoteAsync(CancellationToken.None);

        var channel = Assert.Single(channels);
        Assert.Equal("9.0", channel.Channel);
        Assert.Equal("9.0.100", channel.LatestSdkVersion);
    }

    [Fact]
    public async Task Install_OnWindows_VerifiesTheScriptSignatureBeforeRunningIt()
    {
        using var sandbox = new TestSandbox(TestPlatform.Windows);
        var environment = sandbox.CreateEnvironment();
        var pwsh = PlantPwsh(sandbox, environment);
        var config = new InMemoryConfigStore(DotVerConfig.Empty);
        var inventory = new InventoryService(DotVerPaths.From(environment), environment, config);
        var http = new FakeHttpResourceClient("{}", allowDownloads: true);
        var process = new FakeProcessRunner();
        var service = new InstallService(DotVerPaths.From(environment), environment, http, process, inventory);

        // The fake installer installs nothing, so validation fails afterwards;
        // the process log is what this test is about.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.InstallAsync("8.0.100", options: null, CancellationToken.None));

        Assert.Equal(2, process.Invocations.Count);
        var check = process.Invocations[0];
        var install = process.Invocations[1];
        Assert.Equal(pwsh, check.FileName);
        Assert.Contains("-Command", check.Arguments);
        Assert.Contains(check.Arguments, argument => argument.Contains("Get-AuthenticodeSignature", StringComparison.Ordinal));
        var scriptPath = Path.Combine(DotVerPaths.From(environment).Scripts, "dotnet-install.ps1");
        Assert.Equal(scriptPath, check.EnvironmentVariables?["DOTVER_SCRIPT"]);
        Assert.Contains("-File", install.Arguments);
        Assert.Contains(scriptPath, install.Arguments);
    }

    [Fact]
    public async Task Install_OnWindows_AbortsAndDeletesTheScriptWhenTheSignatureCheckFails()
    {
        using var sandbox = new TestSandbox(TestPlatform.Windows);
        var environment = sandbox.CreateEnvironment();
        PlantPwsh(sandbox, environment);
        var config = new InMemoryConfigStore(DotVerConfig.Empty);
        var inventory = new InventoryService(DotVerPaths.From(environment), environment, config);
        var http = new FakeHttpResourceClient("{}", allowDownloads: true);
        var process = new FakeProcessRunner(spec => spec.Arguments.Contains("-Command")
            ? new ProcessResult(1, string.Empty, "Signature status: NotSigned")
            : new ProcessResult(0, string.Empty, string.Empty));
        var service = new InstallService(DotVerPaths.From(environment), environment, http, process, inventory);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.InstallAsync("8.0.100", options: null, CancellationToken.None));

        Assert.Contains("signature", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Single(process.Invocations);
        Assert.False(File.Exists(Path.Combine(DotVerPaths.From(environment).Scripts, "dotnet-install.ps1")));
    }

    private static string PlantPwsh(TestSandbox sandbox, TestEnvironmentContext environment)
    {
        var tools = sandbox.CreateDirectory("tools");
        var pwsh = Path.Combine(tools, "pwsh.exe");
        File.WriteAllText(pwsh, string.Empty);
        environment.SetUserEnvironmentVariable("PATH", tools);
        return pwsh;
    }

    private sealed class FakeHttpResourceClient(string payload, bool allowDownloads = false) : IHttpResourceClient
    {
        public Task<string> GetStringAsync(string url, CancellationToken cancellationToken) =>
            Task.FromResult(payload);

        public Task DownloadFileAsync(string url, string destinationPath, CancellationToken cancellationToken)
        {
            if (!allowDownloads)
            {
                throw new InvalidOperationException("Download must not happen for invalid remote metadata.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.WriteAllText(destinationPath, "# fake dotnet-install script");
            return Task.CompletedTask;
        }
    }
}

