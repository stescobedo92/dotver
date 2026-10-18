using DotVer.Application;
using DotVer.Domain;

namespace DotVer.Tests;

public sealed class InventoryCacheTests
{
    [Fact]
    public async Task SecondScanServesFromCacheAndInvalidatesOnChange()
    {
        using var sandbox = new TestSandbox(TestPlatform.Windows);
        sandbox.CreateSdkInstall("8.0.412", isManaged: true);
        var environment = sandbox.CreateEnvironment();
        var paths = DotVerPaths.From(environment);
        var config = new InMemoryConfigStore(DotVerConfig.Empty);
        var inventory = new InventoryService(paths, environment, config);

        var first = await inventory.ScanAsync(CancellationToken.None);
        Assert.Contains(first, sdk => sdk.Version.Raw == "8.0.412");
        var cacheFile = Path.Combine(paths.Home, "cache", "inventory.json");
        Assert.True(File.Exists(cacheFile));

        var second = await inventory.ScanAsync(CancellationToken.None);
        Assert.Equal(first.Count, second.Count);

        // Installing another SDK changes directory mtimes: cache invalidates.
        sandbox.CreateSdkInstall("9.0.100", isManaged: true);
        Directory.SetLastWriteTimeUtc(
            Path.Combine(paths.ManagedInstalls, "9.0.100", "sdk"),
            DateTime.UtcNow.AddMinutes(1));
        var third = await inventory.ScanAsync(CancellationToken.None);
        Assert.Contains(third, sdk => sdk.Version.Raw == "9.0.100");
    }

    [Fact]
    public async Task TamperedCacheIsDiscarded()
    {
        using var sandbox = new TestSandbox(TestPlatform.Windows);
        sandbox.CreateSdkInstall("8.0.412", isManaged: true);
        var environment = sandbox.CreateEnvironment();
        var paths = DotVerPaths.From(environment);
        var config = new InMemoryConfigStore(DotVerConfig.Empty);
        var inventory = new InventoryService(paths, environment, config);

        await inventory.ScanAsync(CancellationToken.None);
        var cacheFile = Path.Combine(paths.Home, "cache", "inventory.json");
        var payload = await File.ReadAllTextAsync(cacheFile);

        // Rewrite the cached version to one that does not exist on disk: the
        // loader must reject the pair (layout validation) and rescan instead.
        var tampered = payload.Replace("8.0.412", "8.0.999", StringComparison.Ordinal);
        Assert.NotEqual(payload, tampered);
        await File.WriteAllTextAsync(cacheFile, tampered);

        var scanned = await inventory.ScanAsync(CancellationToken.None);
        Assert.Contains(scanned, sdk => sdk.Version.Raw == "8.0.412" && sdk.DotnetPath.EndsWith("dotnet.exe", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(scanned, sdk => sdk.Version.Raw == "8.0.999");
    }

    [Fact]
    public async Task InventedRootAndCorruptEntriesAreDiscarded()
    {
        using var sandbox = new TestSandbox(TestPlatform.Windows);
        sandbox.CreateSdkInstall("8.0.412", isManaged: true);
        var environment = sandbox.CreateEnvironment();
        var paths = DotVerPaths.From(environment);
        var inventory = new InventoryService(paths, environment, new InMemoryConfigStore(DotVerConfig.Empty));
        await inventory.ScanAsync(CancellationToken.None);

        var cacheFile = Path.Combine(paths.Home, "cache", "inventory.json");
        var payload = await File.ReadAllTextAsync(cacheFile);
        using var doc = System.Text.Json.JsonDocument.Parse(payload);
        var fingerprint = doc.RootElement.GetProperty("fingerprint").GetString();

        // Invented root outside every candidate set + NUL-bearing root: both
        // must be discarded (full rescan), never served, never crash.
        var evil = Path.Combine(sandbox.Root, "evil-repo");
        Directory.CreateDirectory(Path.Combine(evil, "sdk", "9.0.999"));
        File.WriteAllText(Path.Combine(evil, "dotnet.exe"), string.Empty);
        var poisoned = "{\"schemaVersion\":2,\"fingerprint\":\"" + fingerprint + "\",\"sdks\":[" +
            "{\"root\":\"" + evil.Replace("\\", "\\\\") + "\",\"version\":\"9.0.999\"}," +
            "{\"root\":\"bad\\u0000root\",\"version\":\"8.0.412\"}]}";
        await File.WriteAllTextAsync(cacheFile, poisoned);

        var scanned = await inventory.ScanAsync(CancellationToken.None);
        Assert.DoesNotContain(scanned, sdk => sdk.Version.Raw == "9.0.999");
        Assert.Contains(scanned, sdk => sdk.Version.Raw == "8.0.412");
    }

    [Fact]
    public async Task OmittedPairsAreDiscardedByCompletenessCheck()
    {
        using var sandbox = new TestSandbox(TestPlatform.Windows);
        sandbox.CreateSdkInstall("8.0.412", isManaged: true);
        sandbox.CreateSdkInstall("9.0.100", isManaged: true);
        var environment = sandbox.CreateEnvironment();
        var paths = DotVerPaths.From(environment);
        var inventory = new InventoryService(paths, environment, new InMemoryConfigStore(DotVerConfig.Empty));
        await inventory.ScanAsync(CancellationToken.None);

        var cacheFile = Path.Combine(paths.Home, "cache", "inventory.json");
        var payload = await File.ReadAllTextAsync(cacheFile);
        using var doc = System.Text.Json.JsonDocument.Parse(payload);
        var fingerprint = doc.RootElement.GetProperty("fingerprint").GetString();
        var root = Path.Combine(paths.ManagedInstalls, "8.0.412").Replace("\\", "\\\\");
        var subset = "{\"schemaVersion\":2,\"fingerprint\":\"" + fingerprint + "\",\"sdks\":[" +
            "{\"root\":\"" + root + "\",\"version\":\"8.0.412\"}]}";
        await File.WriteAllTextAsync(cacheFile, subset);

        var scanned = await inventory.ScanAsync(CancellationToken.None);
        Assert.Contains(scanned, sdk => sdk.Version.Raw == "8.0.412");
        Assert.Contains(scanned, sdk => sdk.Version.Raw == "9.0.100");
    }

    [Fact]
    public async Task CaseVariantCachedVersionIsDiscarded()
    {
        using var sandbox = new TestSandbox(TestPlatform.Windows);
        sandbox.CreateSdkInstall("8.0.100-rc.1", isManaged: true);
        var environment = sandbox.CreateEnvironment();
        var paths = DotVerPaths.From(environment);
        var inventory = new InventoryService(paths, environment, new InMemoryConfigStore(DotVerConfig.Empty));
        await inventory.ScanAsync(CancellationToken.None);

        var cacheFile = Path.Combine(paths.Home, "cache", "inventory.json");
        var payload = await File.ReadAllTextAsync(cacheFile);
        await File.WriteAllTextAsync(cacheFile, payload.Replace("8.0.100-rc.1", "8.0.100-RC.1", StringComparison.Ordinal));

        var scanned = await inventory.ScanAsync(CancellationToken.None);
        Assert.Contains(scanned, sdk => sdk.Version.Raw == "8.0.100-rc.1");
        Assert.DoesNotContain(scanned, sdk => sdk.Version.Raw == "8.0.100-RC.1");
    }

    [Fact]
    public async Task NoCacheEnvironmentDisablesCacheFile()
    {
        using var sandbox = new TestSandbox(TestPlatform.Windows);
        sandbox.CreateSdkInstall("8.0.412", isManaged: true);
        var environment = sandbox.CreateEnvironment();
        environment.SetUserEnvironmentVariable("DOTVER_NO_CACHE", "1");
        var paths = DotVerPaths.From(environment);
        var inventory = new InventoryService(paths, environment, new InMemoryConfigStore(DotVerConfig.Empty));

        var scanned = await inventory.ScanAsync(CancellationToken.None);

        Assert.Contains(scanned, sdk => sdk.Version.Raw == "8.0.412");
        Assert.False(File.Exists(Path.Combine(paths.Home, "cache", "inventory.json")));
    }
}
