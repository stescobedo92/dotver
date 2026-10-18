using DotVer.Application;
using DotVer.Domain;

namespace DotVer.Tests;

public sealed class CompletionTests
{
    [Theory]
    [InlineData(ShellKind.PowerShell, "Register-ArgumentCompleter")]
    [InlineData(ShellKind.Bash, "complete -F _dotver dotver")]
    [InlineData(ShellKind.Zsh, "compdef _dotver dotver")]
    [InlineData(ShellKind.Fish, "complete -c dotver")]
    public void Render_ProducesShellScriptWithoutEval(ShellKind shell, string expectedFragment)
    {
        var script = CompletionScripts.Render(shell);

        Assert.Contains(expectedFragment, script, StringComparison.Ordinal);
        Assert.Contains("__complete", script, StringComparison.Ordinal);
        Assert.DoesNotContain("eval", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("outdated", script, StringComparison.Ordinal);
        Assert.Contains("deactivate", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_Bash_NeverReexpandsDynamicOutput()
    {
        var script = CompletionScripts.Render(ShellKind.Bash);

        Assert.DoesNotContain("compgen -W \"$(dotver", script, StringComparison.Ordinal);
        Assert.Contains("sdk-tokens", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetCandidates_FiltersMaliciousVersionRawFromInventory()
    {
        var malicious = new SdkInstallation(
            new SdkVersion("$(id)", 8, 0, 400, 0),
            "root",
            "sdk",
            "dotnet",
            SdkOriginKind.ManagedInstall,
            "test",
            true,
            true,
            "test");
        var good = new SdkInstallation(
            SdkVersion.Parse("8.0.412"),
            "root",
            "sdk",
            "dotnet",
            SdkOriginKind.ManagedInstall,
            "test",
            true,
            true,
            "test");
        var service = new CompletionDataService(
            new StubInventory([malicious, good]),
            new InMemoryConfigStore(DotVerConfig.Empty));

        var versions = await service.GetCandidatesAsync("versions", CancellationToken.None);
        var sdkTokens = await service.GetCandidatesAsync("sdk-tokens", CancellationToken.None);

        Assert.Equal(["8.0.412"], versions);
        Assert.DoesNotContain(sdkTokens, static token => token.Contains('$'));
        Assert.DoesNotContain("system", sdkTokens);
    }

    [Fact]
    public async Task AddAlias_WithReservedName_Throws()
    {
        using var sandbox = new TestSandbox(TestPlatform.Windows);
        sandbox.CreateSdkInstall("8.0.412", isManaged: true);
        var configStore = new InMemoryConfigStore(DotVerConfig.Empty);
        var environment = sandbox.CreateEnvironment();
        var inventory = new InventoryService(DotVerPaths.From(environment), environment, configStore);
        var state = new StateService(configStore, new VersionResolver(), inventory, DotVerPaths.From(environment), environment);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => state.AddAliasAsync("system", "8.0.412", CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => state.AddAliasAsync("latest", "8.0.412", CancellationToken.None));
    }

    private sealed class StubInventory(IReadOnlyList<SdkInstallation> sdks) : IInventoryService
    {
        public Task<IReadOnlyList<SdkInstallation>> ScanAsync(CancellationToken cancellationToken) =>
            Task.FromResult(sdks);

        public string? GetSystemDotnetPath() => null;
    }

    [Fact]
    public void Render_Cmd_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CompletionScripts.Render(ShellKind.Cmd));
    }

    [Fact]
    public async Task GetCandidates_Tokens_FiltersUnsafeAliases()
    {
        using var sandbox = new TestSandbox(TestPlatform.Windows);
        sandbox.CreateSdkInstall("8.0.412", isManaged: true);
        var environment = sandbox.CreateEnvironment();
        var config = new InMemoryConfigStore(new DotVerConfig(
            null,
            null,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["lts"] = "8.0.412",
                ["evil;rm -rf"] = "8.0.412",
                ["with space"] = "8.0.412",
                ["$(boom)"] = "8.0.412"
            },
            []));
        var inventory = new InventoryService(DotVerPaths.From(environment), environment, config);
        var service = new CompletionDataService(inventory, config);

        var tokens = await service.GetCandidatesAsync("tokens", CancellationToken.None);

        Assert.Contains("8.0.412", tokens);
        Assert.Contains("lts", tokens);
        Assert.Contains("system", tokens);
        Assert.DoesNotContain(tokens, static token => token.Contains(';') || token.Contains(' ') || token.Contains('$'));
    }

    [Fact]
    public async Task GetCandidates_UnknownKind_ReturnsEmpty()
    {
        using var sandbox = new TestSandbox(TestPlatform.Windows);
        var environment = sandbox.CreateEnvironment();
        var config = new InMemoryConfigStore(DotVerConfig.Empty);
        var inventory = new InventoryService(DotVerPaths.From(environment), environment, config);
        var service = new CompletionDataService(inventory, config);

        Assert.Empty(await service.GetCandidatesAsync("bogus", CancellationToken.None));
    }

    [Fact]
    public async Task ClearCurrentAndDefault_RemovesPersistedSelection()
    {
        using var sandbox = new TestSandbox(TestPlatform.Windows);
        sandbox.CreateSdkInstall("8.0.412", isManaged: true);
        var configStore = new InMemoryConfigStore(new DotVerConfig(
            "8.0.412",
            "8.0.412",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            []));
        var environment = sandbox.CreateEnvironment();
        var inventory = new InventoryService(DotVerPaths.From(environment), environment, configStore);
        var state = new StateService(configStore, new VersionResolver(), inventory, DotVerPaths.From(environment), environment);

        Assert.True(await state.ClearCurrentAsync(CancellationToken.None));
        Assert.True(await state.ClearDefaultAsync(CancellationToken.None));
        Assert.False(await state.ClearCurrentAsync(CancellationToken.None));

        var saved = await configStore.LoadAsync(CancellationToken.None);
        Assert.Null(saved.CurrentVersion);
        Assert.Null(saved.DefaultVersion);
    }
}
