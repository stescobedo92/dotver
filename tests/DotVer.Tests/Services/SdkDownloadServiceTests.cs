using System.Security.Cryptography;
using System.Text;
using DotVer.Application;
using DotVer.Domain;

namespace DotVer.Tests;

public sealed class SdkDownloadServiceTests
{
    private const string IndexUrl = "https://builds.dotnet.microsoft.com/dotnet/release-metadata/releases-index.json";
    private const string ReleasesUrl = "https://builds.dotnet.microsoft.com/dotnet/release-metadata/8.0/releases.json";
    private const string WinZipUrl = "https://builds.dotnet.microsoft.com/dotnet/Sdk/8.0.427/dotnet-sdk-8.0.427-win-x64.zip";
    private const string LinuxTarUrl = "https://builds.dotnet.microsoft.com/dotnet/Sdk/8.0.427/dotnet-sdk-8.0.427-linux-x64.tar.gz";

    private const string IndexJson =
        """{"releases-index":[{"channel-version":"8.0","latest-sdk":"8.0.427","support-phase":"active","release-type":"lts","releases.json":"https://builds.dotnet.microsoft.com/dotnet/release-metadata/8.0/releases.json"}]}""";

    private static readonly byte[] WinBytes = Encoding.UTF8.GetBytes("fake windows sdk archive");
    private static readonly byte[] LinuxBytes = Encoding.UTF8.GetBytes("fake linux sdk archive");

    private static string Sha512(byte[] bytes) => Convert.ToHexStringLower(SHA512.HashData(bytes));

    private static string ReleasesJson(string? winHash, string? linuxHash, string winUrl = WinZipUrl) =>
        "{\"releases\":[{\"release-version\":\"8.0.33\",\"sdks\":[{\"version\":\"8.0.427\",\"files\":[" +
        "{\"name\":\"dotnet-sdk-win-x64.exe\",\"rid\":\"win-x64\",\"url\":\"https://builds.dotnet.microsoft.com/dotnet/Sdk/8.0.427/dotnet-sdk-8.0.427-win-x64.exe\",\"hash\":\"00\"}," +
        "{\"name\":\"dotnet-sdk-win-x64.zip\",\"rid\":\"win-x64\",\"url\":\"" + winUrl + "\"" + (winHash is null ? string.Empty : ",\"hash\":\"" + winHash.ToUpperInvariant() + "\"") + "}," +
        "{\"name\":\"dotnet-sdk-linux-x64.tar.gz\",\"rid\":\"linux-x64\",\"url\":\"" + LinuxTarUrl + "\"" + (linuxHash is null ? string.Empty : ",\"hash\":\"" + linuxHash + "\"") + "}," +
        "{\"name\":\"dotnet-sdk-osx-arm64.pkg\",\"rid\":\"osx-arm64\",\"url\":\"https://builds.dotnet.microsoft.com/dotnet/Sdk/8.0.427/dotnet-sdk-8.0.427-osx-arm64.pkg\",\"hash\":\"00\"}" +
        "]}]}]}";

    private static (SdkDownloadService Service, RoutedHttpClient Http, TestEnvironmentContext Environment) Create(
        TestSandbox sandbox,
        string releasesJson,
        Dictionary<string, byte[]>? downloads = null)
    {
        var environment = sandbox.CreateEnvironment();
        var config = new InMemoryConfigStore(DotVerConfig.Empty);
        var inventory = new InventoryService(DotVerPaths.From(environment), environment, config);
        var http = new RoutedHttpClient(
            new Dictionary<string, string>(StringComparer.Ordinal) { [IndexUrl] = IndexJson, [ReleasesUrl] = releasesJson },
            downloads ?? new Dictionary<string, byte[]>(StringComparer.Ordinal));
        var install = new InstallService(DotVerPaths.From(environment), environment, http, new FakeProcessRunner(), inventory);
        return (new SdkDownloadService(install, http, environment), http, environment);
    }

    [Fact]
    public async Task Plan_ResolvesTheChannelAndPicksOneArchivePerRid()
    {
        using var sandbox = new TestSandbox(TestPlatform.Linux);
        var (service, http, _) = Create(sandbox, ReleasesJson(Sha512(WinBytes), Sha512(LinuxBytes)));

        var plan = await service.PlanAsync(["8.0"], ["win-x64", "linux-x64"], Path.Combine(sandbox.Root, "out"), CancellationToken.None);

        Assert.True(plan.AllSucceeded);
        Assert.Equal(2, plan.Entries.Count);
        Assert.All(plan.Entries, entry => Assert.Equal("8.0.427", entry.ResolvedVersion));
        Assert.Equal("dotnet-sdk-8.0.427-win-x64.zip", plan.Entries[0].Archive!.FileName);
        Assert.Equal(WinZipUrl, plan.Entries[0].Archive!.Url);
        Assert.Equal(Sha512(WinBytes), plan.Entries[0].Archive!.Sha512);
        Assert.Equal("dotnet-sdk-8.0.427-linux-x64.tar.gz", plan.Entries[1].Archive!.FileName);
        Assert.Equal(Path.Combine(sandbox.Root, "out"), plan.OutputDirectory);
        Assert.Empty(http.Downloads);
        // The channel metadata is fetched once, not once per RID.
        Assert.Equal(1, http.Requests.Count(url => url == ReleasesUrl));
    }

    [Fact]
    public async Task Plan_WithoutRids_UsesTheRidOfThisMachine()
    {
        using var sandbox = new TestSandbox(TestPlatform.Linux);
        var (service, _, _) = Create(sandbox, ReleasesJson(Sha512(WinBytes), Sha512(LinuxBytes)));

        var plan = await service.PlanAsync(["8.0.427"], [], Path.Combine(sandbox.Root, "out"), CancellationToken.None);

        var entry = Assert.Single(plan.Entries);
        Assert.Equal("linux-x64", entry.Rid);
        Assert.Equal("dotnet-sdk-8.0.427-linux-x64.tar.gz", entry.Archive!.FileName);
    }

    [Fact]
    public async Task Plan_ReportsAnEntryPerFailedTokenAndKeepsTheOthers()
    {
        using var sandbox = new TestSandbox(TestPlatform.Linux);
        var (service, _, _) = Create(sandbox, ReleasesJson(Sha512(WinBytes), Sha512(LinuxBytes)));

        var plan = await service.PlanAsync(["8.0.999", "9.0", "8.0"], ["linux-x64", "osx-arm64"], Path.Combine(sandbox.Root, "out"), CancellationToken.None);

        Assert.Equal(6, plan.Entries.Count);
        Assert.False(plan.AllSucceeded);
        Assert.Contains("does not publish SDK 8.0.999", plan.Entries[0].Error, StringComparison.Ordinal);
        Assert.Contains("Could not resolve '9.0'", plan.Entries[2].Error, StringComparison.Ordinal);
        Assert.True(plan.Entries[4].Planned);
        // osx-arm64 only has a .pkg in this metadata: no .tar.gz means no archive.
        Assert.Contains("no .tar.gz archive", plan.Entries[5].Error, StringComparison.Ordinal);
        // 8.0.999 x2 RIDs, 9.0 x2 RIDs, osx-arm64 without a tarball: five failures, one plan.
        Assert.Equal(5, plan.Failed);
    }

    [Fact]
    public async Task Plan_RejectsAnUnknownRidBeforeTouchingTheNetwork()
    {
        using var sandbox = new TestSandbox(TestPlatform.Linux);
        var (service, http, _) = Create(sandbox, ReleasesJson(Sha512(WinBytes), Sha512(LinuxBytes)));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.PlanAsync(["8.0"], ["windows-x64"], Path.Combine(sandbox.Root, "out"), CancellationToken.None));

        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task Plan_FallsBackToABuiltNameWhenTheUrlFileNameIsNotPlain()
    {
        using var sandbox = new TestSandbox(TestPlatform.Linux);
        var (service, _, _) = Create(sandbox, ReleasesJson(Sha512(WinBytes), Sha512(LinuxBytes),
            winUrl: "https://builds.dotnet.microsoft.com/dotnet/Sdk/8.0.427/%2e%2e%5cevil.zip"));

        var plan = await service.PlanAsync(["8.0"], ["win-x64"], Path.Combine(sandbox.Root, "out"), CancellationToken.None);

        var entry = Assert.Single(plan.Entries);
        Assert.Equal("dotnet-sdk-8.0.427-win-x64.zip", entry.Archive!.FileName);
    }

    [Fact]
    public async Task Download_VerifiesTheSha512AndWritesTheSidecar()
    {
        using var sandbox = new TestSandbox(TestPlatform.Linux);
        var downloads = new Dictionary<string, byte[]>(StringComparer.Ordinal) { [WinZipUrl] = WinBytes, [LinuxTarUrl] = LinuxBytes };
        var (service, http, _) = Create(sandbox, ReleasesJson(Sha512(WinBytes), Sha512(LinuxBytes)), downloads);
        var output = Path.Combine(sandbox.Root, "archives");
        var progress = new List<DownloadProgress>();

        var result = await service.DownloadAsync(["8.0"], ["win-x64", "linux-x64"], output, new ListProgress(progress), CancellationToken.None);

        Assert.True(result.AllSucceeded);
        Assert.Equal(2, result.Entries.Count);
        var win = result.Entries[0].Download!;
        Assert.Equal(Path.Combine(output, "dotnet-sdk-8.0.427-win-x64.zip"), win.Path);
        Assert.Equal(WinBytes, File.ReadAllBytes(win.Path));
        Assert.Equal(WinBytes.Length, win.Bytes);
        Assert.False(win.SkippedExisting);
        Assert.Equal(Sha512(WinBytes) + "  dotnet-sdk-8.0.427-win-x64.zip\n", File.ReadAllText(win.Path + ".sha512"));
        Assert.False(File.Exists(win.Path + ".part"));
        Assert.Equal([WinZipUrl, LinuxTarUrl], http.Downloads);
        Assert.Contains(progress, update => update.FileName == "dotnet-sdk-8.0.427-win-x64.zip" && update.BytesReceived == WinBytes.Length);
    }

    [Fact]
    public async Task Download_DiscardsAnArchiveWhoseSha512DoesNotMatch()
    {
        using var sandbox = new TestSandbox(TestPlatform.Linux);
        var downloads = new Dictionary<string, byte[]>(StringComparer.Ordinal) { [LinuxTarUrl] = Encoding.UTF8.GetBytes("tampered") };
        var (service, _, _) = Create(sandbox, ReleasesJson(Sha512(WinBytes), Sha512(LinuxBytes)), downloads);
        var output = Path.Combine(sandbox.Root, "archives");

        var result = await service.DownloadAsync(["8.0"], ["linux-x64"], output, progress: null, CancellationToken.None);

        var entry = Assert.Single(result.Entries);
        Assert.Contains("Checksum mismatch", entry.Error, StringComparison.Ordinal);
        Assert.Null(entry.Download);
        Assert.Empty(Directory.EnumerateFiles(output));
    }

    [Fact]
    public async Task Download_SkipsAnArchiveThatIsAlreadyVerifiedOnDisk()
    {
        using var sandbox = new TestSandbox(TestPlatform.Linux);
        var (service, http, _) = Create(sandbox, ReleasesJson(Sha512(WinBytes), Sha512(LinuxBytes)));
        var output = Path.Combine(sandbox.Root, "archives");
        Directory.CreateDirectory(output);
        File.WriteAllBytes(Path.Combine(output, "dotnet-sdk-8.0.427-linux-x64.tar.gz"), LinuxBytes);

        var result = await service.DownloadAsync(["8.0"], ["linux-x64"], output, progress: null, CancellationToken.None);

        var entry = Assert.Single(result.Entries);
        Assert.True(entry.Download!.SkippedExisting);
        Assert.Empty(http.Downloads);
        Assert.True(File.Exists(entry.Download.Path + ".sha512"));
    }

    [Fact]
    public async Task Download_RefusesAnArchiveWithoutAPublishedSha512()
    {
        using var sandbox = new TestSandbox(TestPlatform.Linux);
        var downloads = new Dictionary<string, byte[]>(StringComparer.Ordinal) { [LinuxTarUrl] = LinuxBytes };
        var (service, http, _) = Create(sandbox, ReleasesJson(Sha512(WinBytes), linuxHash: null), downloads);

        var result = await service.DownloadAsync(["8.0"], ["linux-x64"], Path.Combine(sandbox.Root, "archives"), progress: null, CancellationToken.None);

        var entry = Assert.Single(result.Entries);
        Assert.Contains("SHA-512", entry.Error, StringComparison.Ordinal);
        Assert.Empty(http.Downloads);
    }

    [Fact]
    public void ParseChannelSdks_IgnoresEntriesThatAreNotHttpsOrNotObjects()
    {
        const string payload =
            """{"releases":[{"sdks":[{"version":"8.0.100","files":[{"name":"a.zip","rid":"win-x64","url":"http://builds.dotnet.microsoft.com/a.zip","hash":"00"},"junk",{"name":"b.zip","rid":"win-x64","url":"https://builds.dotnet.microsoft.com/b.zip","hash":"zz"}]}]},"junk"]}""";

        var sdks = SdkDownloadService.ParseChannelSdks(payload, "8.0");

        var files = Assert.Single(sdks)!.Value;
        var file = Assert.Single(files);
        Assert.Equal("https://builds.dotnet.microsoft.com/b.zip", file.Url);
        Assert.Null(file.Sha512);
    }

    private sealed class ListProgress(List<DownloadProgress> sink) : IProgress<DownloadProgress>
    {
        public void Report(DownloadProgress value) => sink.Add(value);
    }

    /// <summary>Serves fixed pages and byte payloads per URL and records every request.</summary>
    private sealed class RoutedHttpClient(Dictionary<string, string> pages, Dictionary<string, byte[]> downloads) : IHttpResourceClient
    {
        public List<string> Requests { get; } = [];

        public List<string> Downloads { get; } = [];

        public Task<string> GetStringAsync(string url, CancellationToken cancellationToken)
        {
            Requests.Add(url);
            return pages.TryGetValue(url, out var page)
                ? Task.FromResult(page)
                : throw new HttpRequestException("404 " + url);
        }

        public Task DownloadFileAsync(string url, string destinationPath, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Script downloads are not used by this test.");

        public async Task<long> DownloadToFileAsync(string url, string destinationPath, long maxBytes, Action<long, long?>? onProgress, CancellationToken cancellationToken)
        {
            Requests.Add(url);
            Downloads.Add(url);
            if (!downloads.TryGetValue(url, out var bytes))
            {
                throw new HttpRequestException("404 " + url);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            await File.WriteAllBytesAsync(destinationPath, bytes, cancellationToken);
            onProgress?.Invoke(bytes.Length, bytes.Length);
            return bytes.Length;
        }
    }
}
