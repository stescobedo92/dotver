using System.Net;
using System.Text;
using DotVer.Application;
using DotVer.Domain;
using DotVer.Infrastructure;

namespace DotVer.Tests;

public sealed class AuditServiceTests
{
    [Fact]
    public async Task GetAudit_FlagsSdkOlderThanSecurityRelease()
    {
        using var sandbox = new TestSandbox(TestPlatform.Windows);
        var old = sandbox.CreateSdkInstall("8.0.400", isManaged: true);
        var current = sandbox.CreateSdkInstall("8.0.412", isManaged: true);
        var metadata = new FakeMetadata(new Dictionary<string, IReadOnlyList<ChannelRelease>>
        {
            ["8.0"] =
            [
                new ChannelRelease("8.0.8", IsSecurity: false, ["8.0.400"], []),
                new ChannelRelease("8.0.10", IsSecurity: true, ["8.0.412", "8.0.110"], ["CVE-2024-43483", "CVE-2024-43484"])
            ]
        });
        var service = new AuditService(new StubInventory([old, current]), metadata);

        var entries = await service.GetAuditAsync(CancellationToken.None);

        var oldEntry = entries.Single(e => e.Installation.Version.Raw == "8.0.400");
        Assert.True(oldEntry.IsVulnerable);
        Assert.Equal("8.0.412", oldEntry.FixedInSdk);
        Assert.Equal(["CVE-2024-43483", "CVE-2024-43484"], oldEntry.CveIds);

        var currentEntry = entries.Single(e => e.Installation.Version.Raw == "8.0.412");
        Assert.False(currentEntry.IsVulnerable);
        Assert.True(currentEntry.MetadataKnown);
    }

    [Fact]
    public async Task GetAudit_UnknownVersion_NotFlaggedButMarkedUnknown()
    {
        using var sandbox = new TestSandbox(TestPlatform.Windows);
        var daily = sandbox.CreateSdkInstall("8.0.999", isManaged: true);
        var metadata = new FakeMetadata(new Dictionary<string, IReadOnlyList<ChannelRelease>>
        {
            ["8.0"] = [new ChannelRelease("8.0.10", IsSecurity: true, ["8.0.412"], ["CVE-2024-43483"])]
        });
        var service = new AuditService(new StubInventory([daily]), metadata);

        var entry = Assert.Single(await service.GetAuditAsync(CancellationToken.None));

        Assert.False(entry.MetadataKnown);
        Assert.False(entry.IsVulnerable);
    }

    [Fact]
    public async Task GetAudit_PatchedBandSdk_NotFlaggedBySiblingBandSdk()
    {
        using var sandbox = new TestSandbox(TestPlatform.Windows);
        var patched100 = sandbox.CreateSdkInstall("8.0.132", isManaged: true);
        var old100 = sandbox.CreateSdkInstall("8.0.131", isManaged: true);
        var metadata = new FakeMetadata(new Dictionary<string, IReadOnlyList<ChannelRelease>>
        {
            ["8.0"] =
            [
                new ChannelRelease("8.0.31", IsSecurity: false, ["8.0.425", "8.0.131"], []),
                new ChannelRelease("8.0.32", IsSecurity: true, ["8.0.426", "8.0.132"], ["CVE-2024-43483"])
            ]
        });
        var service = new AuditService(new StubInventory([patched100, old100]), metadata);

        var entries = await service.GetAuditAsync(CancellationToken.None);

        Assert.False(entries.Single(e => e.Installation.Version.Raw == "8.0.132").IsVulnerable);
        var vulnerable = entries.Single(e => e.Installation.Version.Raw == "8.0.131");
        Assert.True(vulnerable.IsVulnerable);
        Assert.Equal("8.0.132", vulnerable.FixedInSdk);
    }

    [Fact]
    public async Task GetAudit_RetiredBand_StillFlaggedVulnerable()
    {
        using var sandbox = new TestSandbox(TestPlatform.Windows);
        var retired = sandbox.CreateSdkInstall("9.0.205", isManaged: true);
        var metadata = new FakeMetadata(new Dictionary<string, IReadOnlyList<ChannelRelease>>
        {
            ["9.0"] =
            [
                new ChannelRelease("9.0.6", IsSecurity: false, ["9.0.205", "9.0.110"], []),
                new ChannelRelease("9.0.9", IsSecurity: true, ["9.0.306", "9.0.111"], ["CVE-2025-21172"])
            ]
        });
        var service = new AuditService(new StubInventory([retired]), metadata);

        var entry = Assert.Single(await service.GetAuditAsync(CancellationToken.None));

        Assert.True(entry.MetadataKnown);
        Assert.True(entry.IsVulnerable);
        Assert.Equal("9.0.306", entry.FixedInSdk);
        Assert.Equal(["CVE-2025-21172"], entry.CveIds);
    }

    [Fact]
    public async Task ListChannelReleases_RefusesNonMicrosoftHost()
    {
        var service = MetadataService(
            """{"releases-index":[{"channel-version":"8.0","latest-sdk":"8.0.412","releases.json":"https://evil.example.com/releases.json"}]}""",
            "{}");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ListChannelReleasesAsync("8.0", CancellationToken.None));
    }

    [Fact]
    public async Task ListChannelReleases_ParsesSingularSdkAndFiltersMalformedEntries()
    {
        var service = MetadataService(
            """{"releases-index":[{"channel-version":"8.0","latest-sdk":"8.0.412","releases.json":"https://builds.dotnet.microsoft.com/dotnet/release-metadata/8.0/releases.json"}]}""",
            """
            {"releases":[
              {"release-version":"8.0.10","security":true,
               "cve-list":[{"cve-id":"CVE-2024-43483"},{"cve-id":"bogus;rm"}],
               "sdks":[{"version":"8.0.412"},{"version":"$(evil)"}]},
              "junk",
              {"release-version":"2.1.30","security":true,"cve-list":[{"cve-id":"CVE-2020-1108"}],"sdk":{"version":"2.1.526"}}
            ]}
            """);

        var releases = await service.ListChannelReleasesAsync("8.0", CancellationToken.None);

        Assert.Equal(2, releases.Count);
        Assert.Equal(["8.0.412"], releases[0].SdkVersions);
        Assert.Equal(["CVE-2024-43483"], releases[0].CveIds);
        Assert.Equal(["2.1.526"], releases[1].SdkVersions);
    }

    private static ReleaseMetadataService MetadataService(string indexPayload, string detailPayload) =>
        new(new HttpClient(new StubHandler(indexPayload, detailPayload)));

    private sealed class StubHandler(string indexPayload, string detailPayload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var payload = request.RequestUri!.AbsoluteUri.Contains("releases-index", StringComparison.Ordinal)
                ? indexPayload
                : detailPayload;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class FakeMetadata(IReadOnlyDictionary<string, IReadOnlyList<ChannelRelease>> byChannel) : IReleaseMetadataService
    {
        public Task<IReadOnlyList<ChannelRelease>> ListChannelReleasesAsync(string channel, CancellationToken cancellationToken) =>
            Task.FromResult(byChannel.TryGetValue(channel, out var releases) ? releases : []);
    }

    private sealed class StubInventory(IReadOnlyList<SdkInstallation> sdks) : IInventoryService
    {
        public Task<IReadOnlyList<SdkInstallation>> ScanAsync(CancellationToken cancellationToken) =>
            Task.FromResult(sdks);

        public string? GetSystemDotnetPath() => null;
    }

}
