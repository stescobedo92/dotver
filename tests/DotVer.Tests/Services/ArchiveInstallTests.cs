using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using DotVer.Application;
using DotVer.Domain;

namespace DotVer.Tests;

public sealed class ArchiveInstallTests
{
    [Fact]
    public async Task InstallFromArchive_ExtractsVerifiedZipIntoTheManagedRoot()
    {
        using var sandbox = new TestSandbox(TestPlatform.Windows);
        var archive = CreateZip(sandbox, "dotnet-sdk-8.0.412-win-x64.zip", zip =>
        {
            AddText(zip, "dotnet.exe", "host");
            AddText(zip, "sdk/8.0.412/dotnet.dll", "sdk");
            AddText(zip, "shared/Microsoft.NETCore.App/8.0.12/x.dll", "runtime");
        });
        var harness = new Harness(sandbox);

        var outcome = await harness.Service.InstallFromArchiveAsync(
            "8.0.412", archive, new ArchiveChecksum("sha256", Sha256(archive)), options: null, CancellationToken.None);

        Assert.False(outcome.WasAlreadyInstalled);
        Assert.Equal("8.0.412", outcome.Installation.Version.Raw);
        Assert.True(outcome.Installation.IsManaged);
        var root = Path.Combine(harness.Paths.ManagedInstalls, "8.0.412");
        Assert.True(File.Exists(Path.Combine(root, "dotnet.exe")));
        Assert.True(File.Exists(Path.Combine(root, "sdk", "8.0.412", "dotnet.dll")));
        Assert.Empty(Directory.EnumerateDirectories(harness.Paths.ManagedInstalls, ".staging-*"));
        Assert.Empty(harness.Process.Invocations);
    }

    [Fact]
    public async Task InstallFromArchive_WithWrongChecksum_ExtractsNothing()
    {
        using var sandbox = new TestSandbox(TestPlatform.Windows);
        var archive = CreateZip(sandbox, "sdk.zip", zip =>
        {
            AddText(zip, "dotnet.exe", "host");
            AddText(zip, "sdk/8.0.412/dotnet.dll", "sdk");
        });
        var harness = new Harness(sandbox);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Service.InstallFromArchiveAsync(
            "8.0.412", archive, new ArchiveChecksum("sha256", new string('0', 64)), options: null, CancellationToken.None));

        Assert.Contains("checksum", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(Path.Combine(harness.Paths.ManagedInstalls, "8.0.412")));
        Assert.False(Directory.Exists(harness.Paths.ManagedInstalls) && Directory.EnumerateDirectories(harness.Paths.ManagedInstalls).Any());
    }

    [Fact]
    public async Task InstallFromArchive_RejectsEntriesThatEscapeTheInstallRoot()
    {
        using var sandbox = new TestSandbox(TestPlatform.Windows);
        var archive = CreateZip(sandbox, "evil.zip", zip =>
        {
            AddText(zip, "dotnet.exe", "host");
            AddText(zip, "sdk/8.0.412/dotnet.dll", "sdk");
            AddText(zip, "../evil.txt", "escaped");
        });
        var harness = new Harness(sandbox);

        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Service.InstallFromArchiveAsync(
            "8.0.412", archive, new ArchiveChecksum("sha256", Sha256(archive)), options: null, CancellationToken.None));

        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(harness.Paths.ManagedInstalls)!, "evil.txt")));
        Assert.False(File.Exists(Path.Combine(harness.Paths.ManagedInstalls, "evil.txt")));
        Assert.False(Directory.Exists(Path.Combine(harness.Paths.ManagedInstalls, "8.0.412")));
    }

    [Fact]
    public async Task InstallFromArchive_RejectsArchivesWithoutTheSdkLayout()
    {
        using var sandbox = new TestSandbox(TestPlatform.Windows);
        var archive = CreateZip(sandbox, "notsdk.zip", zip => AddText(zip, "readme.txt", "nope"));
        var harness = new Harness(sandbox);

        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Service.InstallFromArchiveAsync(
            "8.0.412", archive, new ArchiveChecksum("sha256", Sha256(archive)), options: null, CancellationToken.None));

        Assert.False(Directory.Exists(Path.Combine(harness.Paths.ManagedInstalls, "8.0.412")));
    }

    [Fact]
    public async Task InstallFromArchive_ExtractsTarGzAndRejectsLinkEntries()
    {
        using var sandbox = new TestSandbox(TestPlatform.Linux);
        var good = CreateTarGz(sandbox, "dotnet-sdk-8.0.412-linux-x64.tar.gz", tar =>
        {
            tar.WriteEntry(new PaxTarEntry(TarEntryType.Directory, "sdk/"));
            tar.WriteEntry(new PaxTarEntry(TarEntryType.Directory, "sdk/8.0.412/"));
            tar.WriteEntry(RegularFile("dotnet", "host", UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute));
            tar.WriteEntry(RegularFile("sdk/8.0.412/dotnet.dll", "sdk", UnixFileMode.UserRead | UnixFileMode.UserWrite));
        });
        var harness = new Harness(sandbox);

        var outcome = await harness.Service.InstallFromArchiveAsync(
            "8.0.412", good, new ArchiveChecksum("sha512", Sha512(good)), options: null, CancellationToken.None);

        Assert.Equal("8.0.412", outcome.Installation.Version.Raw);
        Assert.True(File.Exists(Path.Combine(harness.Paths.ManagedInstalls, "8.0.412", "dotnet")));

        var evil = CreateTarGz(sandbox, "evil.tar.gz", tar =>
        {
            tar.WriteEntry(RegularFile("dotnet", "host", UnixFileMode.UserRead | UnixFileMode.UserExecute));
            tar.WriteEntry(RegularFile("sdk/9.0.100/dotnet.dll", "sdk", UnixFileMode.UserRead));
            var link = new PaxTarEntry(TarEntryType.SymbolicLink, "sdk/9.0.100/link") { LinkName = "/etc/passwd" };
            tar.WriteEntry(link);
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Service.InstallFromArchiveAsync(
            "9.0.100", evil, new ArchiveChecksum("sha256", Sha256(evil)), options: null, CancellationToken.None));
        Assert.False(Directory.Exists(Path.Combine(harness.Paths.ManagedInstalls, "9.0.100")));
    }

    [Fact]
    public async Task InstallFromArchive_RequiresAnExactVersion()
    {
        using var sandbox = new TestSandbox(TestPlatform.Windows);
        var archive = CreateZip(sandbox, "sdk.zip", zip => AddText(zip, "dotnet.exe", "host"));
        var harness = new Harness(sandbox);

        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Service.InstallFromArchiveAsync(
            "8.0", archive, new ArchiveChecksum("sha256", Sha256(archive)), options: null, CancellationToken.None));
    }

    private static PaxTarEntry RegularFile(string name, string content, UnixFileMode mode)
    {
        var entry = new PaxTarEntry(TarEntryType.RegularFile, name) { Mode = mode };
        entry.DataStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
        return entry;
    }

    private static string CreateZip(TestSandbox sandbox, string name, Action<ZipArchive> fill)
    {
        var path = Path.Combine(sandbox.CreateDirectory("downloads"), name);
        using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            fill(zip);
        }

        return path;
    }

    private static string CreateTarGz(TestSandbox sandbox, string name, Action<TarWriter> fill)
    {
        var path = Path.Combine(sandbox.CreateDirectory("downloads"), name);
        using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
        using (var gzip = new GZipStream(stream, CompressionLevel.Fastest))
        using (var tar = new TarWriter(gzip, TarEntryFormat.Pax, leaveOpen: false))
        {
            fill(tar);
        }

        return path;
    }

    private static void AddText(ZipArchive zip, string entryName, string content)
    {
        var entry = zip.CreateEntry(entryName);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    private static string Sha512(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA512.HashData(stream));
    }

    private sealed class Harness
    {
        public Harness(TestSandbox sandbox)
        {
            var environment = sandbox.CreateEnvironment();
            Paths = DotVerPaths.From(environment);
            var config = new InMemoryConfigStore(DotVerConfig.Empty);
            var inventory = new InventoryService(Paths, environment, config);
            Process = new FakeProcessRunner();
            Service = new InstallService(Paths, environment, new NoHttp(), Process, inventory);
        }

        public DotVerPaths Paths { get; }
        public FakeProcessRunner Process { get; }
        public InstallService Service { get; }
    }

    private sealed class NoHttp : IHttpResourceClient
    {
        public Task<string> GetStringAsync(string url, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Offline installs must not touch the network.");

        public Task DownloadFileAsync(string url, string destinationPath, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Offline installs must not touch the network.");
    }
}
