using System.Text.Json;
using DotVer.Application;
using DotVer.Domain;

namespace DotVer.Tests;

public sealed class JsonReportsTests
{
    [Fact]
    public void ForList_EmitsVersionedEscapedJson()
    {
        using var sandbox = new TestSandbox(TestPlatform.Windows);
        var sdk = sandbox.CreateSdkInstall("8.0.412", isManaged: true);
        var selection = new SdkSelection(sdk, "Selected by default (8.0.412)", false);
        var config = new DotVerConfig("8.0.412", "8.0.412", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), []);

        var json = JsonReports.ForList([sdk], selection, config);

        using var doc = JsonDocument.Parse(json);
        Assert.Equal(1, doc.RootElement.GetProperty("schemaVersion").GetInt32());
        var first = doc.RootElement.GetProperty("sdks")[0];
        Assert.Equal("8.0.412", first.GetProperty("version").GetString());
        Assert.True(first.GetProperty("isCurrent").GetBoolean());
        Assert.True(first.GetProperty("isDefault").GetBoolean());
        Assert.True(first.GetProperty("isManaged").GetBoolean());
    }

    [Fact]
    public void ForCurrent_WhenNoSelection_EmitsNullVersion()
    {
        var json = JsonReports.ForCurrent(new SdkSelection(null, "Selected by system fallback", false));

        using var doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("version").ValueKind);
        Assert.Equal("Selected by system fallback", doc.RootElement.GetProperty("reason").GetString());
        Assert.False(doc.RootElement.GetProperty("fromGlobalJson").GetBoolean());
    }

    [Fact]
    public void ForScan_WithPlannedVersions_EmitsWouldInstall()
    {
        var scan = new WorkspaceScanResult(
            "/repo",
            [new WorkspacePinEntry("/repo/global.json", "9.0.100", null, null, Satisfied: false)],
            Truncated: false);

        var json = JsonReports.ForScan(scan, wouldInstall: ["9.0.100"]);

        using var doc = JsonDocument.Parse(json);
        Assert.Equal("9.0.100", doc.RootElement.GetProperty("wouldInstall")[0].GetString());
        Assert.False(doc.RootElement.TryGetProperty("installedVersions", out _));
    }

    [Fact]
    public void ForDoctor_IncludesWarningsAliasesAndCounts()
    {
        using var sandbox = new TestSandbox(TestPlatform.Windows);
        var sdk = sandbox.CreateSdkInstall("8.0.412", isManaged: true);
        var config = new DotVerConfig(
            null,
            "8.0.412",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["lts"] = "8.0.412" },
            ["C:\\custom\\dotnet"]);
        var selection = new SdkSelection(sdk, "reason \"quoted\" [brackets]", false);

        var json = JsonReports.ForDoctor([sdk], selection, config, ["warn [x]"]);

        using var doc = JsonDocument.Parse(json);
        Assert.Equal(1, doc.RootElement.GetProperty("sdksDetected").GetInt32());
        Assert.Equal(1, doc.RootElement.GetProperty("managedSdks").GetInt32());
        Assert.Equal("8.0.412", doc.RootElement.GetProperty("aliases").GetProperty("lts").GetString());
        Assert.Equal("warn [x]", doc.RootElement.GetProperty("warnings")[0].GetString());
        Assert.Equal("reason \"quoted\" [brackets]", doc.RootElement.GetProperty("currentReason").GetString());
    }

    [Fact]
    public void ForRemote_MapsChannels()
    {
        var json = JsonReports.ForRemote([new ReleaseChannel("8.0", "8.0.412", "active", "lts")]);

        using var doc = JsonDocument.Parse(json);
        var first = doc.RootElement.GetProperty("channels")[0];
        Assert.Equal("8.0", first.GetProperty("channel").GetString());
        Assert.Equal("8.0.412", first.GetProperty("latestSdk").GetString());
        Assert.Equal("lts", first.GetProperty("releaseType").GetString());
    }
}
