using System.Security.Cryptography;
using System.Text;
using DotVer.Application;
using DotVer.Domain;

namespace DotVer.Tests;

public sealed class SelfUpdateServiceTests
{
    [Fact]
    public async Task Check_ReportsNewerReleaseAndTheAssetForThisPlatform()
    {
        using var sandbox = new TestSandbox(TestPlatform.Linux);
        var environment = sandbox.CreateEnvironment();
        environment.ProductVersion = "0.3.0";
        File.WriteAllText(environment.ProcessPath, "old");
        var feed = FeedWith("v0.4.0", ("dotver-linux-x64", "new"));
        var service = new SelfUpdateService(feed, environment);

        var check = await service.CheckAsync(version: null, CancellationToken.None);

        Assert.Equal("0.3.0", check.CurrentVersion);
        Assert.Equal("0.4.0", check.LatestVersion);
        Assert.True(check.UpdateAvailable);
        Assert.True(check.CanSelfUpdate);
        Assert.Equal("dotver-linux-x64", check.AssetName);
        Assert.Equal(environment.ProcessPath, check.ExecutablePath);
    }

    [Fact]
    public async Task Check_WhenAlreadyCurrent_ReportsNoUpdate()
    {
        using var sandbox = new TestSandbox(TestPlatform.Linux);
        var environment = sandbox.CreateEnvironment();
        environment.ProductVersion = "0.4.0";
        File.WriteAllText(environment.ProcessPath, "current");
        var service = new SelfUpdateService(FeedWith("v0.4.0", ("dotver-linux-x64", "same")), environment);

        var check = await service.CheckAsync(version: null, CancellationToken.None);

        Assert.False(check.UpdateAvailable);
        var outcome = await service.UpdateAsync(version: null, CancellationToken.None);
        Assert.False(outcome.Updated);
        Assert.Equal("current", File.ReadAllText(environment.ProcessPath));
    }

    [Fact]
    public async Task Update_OnUnix_ReplacesTheBinaryAfterVerifyingTheChecksum()
    {
        using var sandbox = new TestSandbox(TestPlatform.Linux);
        var environment = sandbox.CreateEnvironment();
        environment.ProductVersion = "0.3.0";
        File.WriteAllText(environment.ProcessPath, "old");
        var feed = FeedWith("v0.4.0", ("dotver-linux-x64", "new binary"));
        var service = new SelfUpdateService(feed, environment);

        var outcome = await service.UpdateAsync(version: null, CancellationToken.None);

        Assert.True(outcome.Updated);
        Assert.Equal("0.4.0", outcome.ToVersion);
        Assert.Equal("new binary", File.ReadAllText(environment.ProcessPath));
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(environment.ProcessPath)!, ".dotver-update-*"));
    }

    [Fact]
    public async Task Update_OnWindows_KeepsTheRunningBinaryAsOld()
    {
        using var sandbox = new TestSandbox(TestPlatform.Windows);
        var environment = sandbox.CreateEnvironment();
        environment.ProductVersion = "0.3.0";
        File.WriteAllText(environment.ProcessPath, "old");
        var service = new SelfUpdateService(FeedWith("v0.4.0", ("dotver-win-x64.exe", "new binary")), environment);

        var outcome = await service.UpdateAsync(version: null, CancellationToken.None);

        Assert.True(outcome.Updated);
        Assert.Equal("new binary", File.ReadAllText(environment.ProcessPath));
        // Windows cannot delete a running image: the old one is parked next to it.
        Assert.Equal("old", File.ReadAllText(environment.ProcessPath + ".old"));
    }

    [Fact]
    public async Task Update_WithChecksumMismatch_LeavesTheBinaryUntouched()
    {
        using var sandbox = new TestSandbox(TestPlatform.Linux);
        var environment = sandbox.CreateEnvironment();
        environment.ProductVersion = "0.3.0";
        File.WriteAllText(environment.ProcessPath, "old");
        var feed = FeedWith("v0.4.0", ("dotver-linux-x64", "tampered"));
        feed.Contents["https://example.test/v0.4.0/checksums.txt"] = Encoding.UTF8.GetBytes("dotver-linux-x64  " + new string('0', 64) + "\n");
        var service = new SelfUpdateService(feed, environment);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateAsync(version: null, CancellationToken.None));

        Assert.Contains("checksum", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("old", File.ReadAllText(environment.ProcessPath));
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(environment.ProcessPath)!, ".dotver-update-*"));
    }

    [Fact]
    public async Task Update_WhenRunningThroughTheDotnetHost_IsRefused()
    {
        using var sandbox = new TestSandbox(TestPlatform.Linux);
        var home = Path.Combine(sandbox.Root, "home");
        Directory.CreateDirectory(home);
        var variables = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["DOTVER_HOME"] = Path.Combine(sandbox.Root, ".dotver"),
            ["PATH"] = string.Empty
        };
        // `dotnet dotver.dll`: the process is the shared host, not a dotver binary.
        var environment = new TestEnvironmentContext(home, sandbox.Root, Path.Combine(sandbox.Root, "dotnet"), variables, TestPlatform.Linux)
        {
            ProductVersion = "0.3.0"
        };
        var service = new SelfUpdateService(FeedWith("v0.4.0", ("dotver-linux-x64", "new")), environment);

        var check = await service.CheckAsync(version: null, CancellationToken.None);

        Assert.False(check.CanSelfUpdate);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateAsync(version: null, CancellationToken.None));
    }

    [Fact]
    public async Task Update_WithExplicitVersion_UsesThatTag()
    {
        using var sandbox = new TestSandbox(TestPlatform.Linux);
        var environment = sandbox.CreateEnvironment();
        environment.ProductVersion = "0.4.0";
        File.WriteAllText(environment.ProcessPath, "current");
        var feed = FeedWith("v0.4.0", ("dotver-linux-x64", "same"));
        feed.AddRelease("v0.3.1", ("dotver-linux-x64", "older build"));
        var service = new SelfUpdateService(feed, environment);

        var outcome = await service.UpdateAsync("0.3.1", CancellationToken.None);

        Assert.True(outcome.Updated);
        Assert.Equal("0.3.1", outcome.ToVersion);
        Assert.Equal("older build", File.ReadAllText(environment.ProcessPath));
    }

    private static FakeReleaseFeed FeedWith(string tag, params (string Name, string Content)[] assets)
    {
        var feed = new FakeReleaseFeed();
        feed.AddRelease(tag, assets);
        feed.LatestTag = tag;
        return feed;
    }

    private sealed class FakeReleaseFeed : IReleaseFeed
    {
        private readonly Dictionary<string, ReleaseInfo> _releases = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, byte[]> Contents { get; } = new(StringComparer.OrdinalIgnoreCase);

        public string LatestTag { get; set; } = string.Empty;

        public void AddRelease(string tag, params (string Name, string Content)[] assets)
        {
            var urls = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var checksums = new StringBuilder();
            foreach (var (name, content) in assets)
            {
                var url = $"https://example.test/{tag}/{name}";
                var bytes = Encoding.UTF8.GetBytes(content);
                Contents[url] = bytes;
                urls[name] = url;
                checksums.Append(name).Append("  ").Append(Convert.ToHexStringLower(SHA256.HashData(bytes))).Append('\n');
            }

            var checksumsUrl = $"https://example.test/{tag}/checksums.txt";
            Contents[checksumsUrl] = Encoding.UTF8.GetBytes(checksums.ToString());
            urls["checksums.txt"] = checksumsUrl;
            _releases[tag] = new ReleaseInfo(tag, tag.TrimStart('v'), urls);
        }

        public Task<ReleaseInfo> GetLatestAsync(string repository, CancellationToken cancellationToken) =>
            Task.FromResult(_releases[LatestTag]);

        public Task<ReleaseInfo> GetByTagAsync(string repository, string tag, CancellationToken cancellationToken) =>
            _releases.TryGetValue(tag, out var release)
                ? Task.FromResult(release)
                : throw new InvalidOperationException($"Release {tag} not found.");

        public Task<byte[]> DownloadAssetAsync(string url, long maxBytes, CancellationToken cancellationToken) =>
            Task.FromResult(Contents[url]);
    }
}
