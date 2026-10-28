using DotVer.Application;

namespace DotVer.Tests;

public sealed class SelfUninstallServiceTests
{
    [Fact]
    public async Task Uninstall_OnUnix_RemovesProfileBlocksAndTheHomeDirectory()
    {
        using var sandbox = new TestSandbox(TestPlatform.Linux);
        var environment = sandbox.CreateEnvironment();
        var bashrc = Path.Combine(environment.HomeDirectory, ".bashrc");
        const string userContent = "# my aliases\nalias ll='ls -la'\n";
        File.WriteAllText(bashrc, userContent);
        var paths = DotVerPaths.From(environment);
        var bootstrap = new EnvironmentBootstrapService(paths, environment, new ShimService(paths, environment));
        await bootstrap.EnsureConfiguredAsync(CancellationToken.None);
        Assert.Contains("# >>> dotver >>>", File.ReadAllText(bashrc), StringComparison.Ordinal);
        var service = new SelfUninstallService(paths, environment, bootstrap);

        var outcome = await service.UninstallAsync(keepSdks: false, CancellationToken.None);

        Assert.True(outcome.Executed);
        Assert.Empty(outcome.Errors);
        Assert.Equal(userContent, File.ReadAllText(bashrc));
        Assert.DoesNotContain("dotver", File.ReadAllText(Path.Combine(environment.HomeDirectory, ".zshrc")), StringComparison.Ordinal);
        Assert.False(Directory.Exists(paths.Home));
        Assert.Contains(outcome.Plan.BinaryPath, outcome.Plan.BinaryRemovalHint, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Uninstall_WithKeepSdks_LeavesInstallsAndRuntimesInPlace()
    {
        using var sandbox = new TestSandbox(TestPlatform.Linux);
        var environment = sandbox.CreateEnvironment();
        sandbox.CreateSdkInstall("8.0.412", isManaged: true);
        var paths = DotVerPaths.From(environment);
        Directory.CreateDirectory(Path.Combine(paths.ManagedRuntimes, "aspnetcore-8.0"));
        var bootstrap = new EnvironmentBootstrapService(paths, environment, new ShimService(paths, environment));
        await bootstrap.EnsureConfiguredAsync(CancellationToken.None);
        var service = new SelfUninstallService(paths, environment, bootstrap);

        var outcome = await service.UninstallAsync(keepSdks: true, CancellationToken.None);

        Assert.True(outcome.Executed);
        Assert.True(Directory.Exists(Path.Combine(paths.ManagedInstalls, "8.0.412")));
        Assert.True(Directory.Exists(paths.ManagedRuntimes));
        Assert.False(Directory.Exists(paths.Shims));
        Assert.False(Directory.Exists(paths.Env));
        Assert.False(File.Exists(Path.Combine(paths.Home, "bootstrap.json")));
    }

    [Fact]
    public async Task Plan_IsADryRunThatChangesNothing()
    {
        using var sandbox = new TestSandbox(TestPlatform.Linux);
        var environment = sandbox.CreateEnvironment();
        var paths = DotVerPaths.From(environment);
        var bootstrap = new EnvironmentBootstrapService(paths, environment, new ShimService(paths, environment));
        await bootstrap.EnsureConfiguredAsync(CancellationToken.None);
        var service = new SelfUninstallService(paths, environment, bootstrap);

        var plan = await service.PlanAsync(keepSdks: false, CancellationToken.None);

        Assert.Contains(plan.ProfilesToClean, profile => profile.EndsWith(".bashrc", StringComparison.Ordinal));
        Assert.Contains(paths.Home, plan.DirectoriesToDelete);
        Assert.True(Directory.Exists(paths.Shims));
        Assert.Contains("# >>> dotver >>>", File.ReadAllText(Path.Combine(environment.HomeDirectory, ".bashrc")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Uninstall_OnWindows_RemovesThePathEntryAndTheUserVariable()
    {
        using var sandbox = new TestSandbox(TestPlatform.Windows);
        var environment = sandbox.CreateEnvironment();
        environment.SetUserEnvironmentVariable("PATH", @"C:\tools;%USERPROFILE%\bin");
        var paths = DotVerPaths.From(environment);
        var bootstrap = new EnvironmentBootstrapService(paths, environment, new ShimService(paths, environment));
        await bootstrap.EnsureConfiguredAsync(CancellationToken.None);
        Assert.StartsWith(paths.Shims, environment.GetUserEnvironmentVariable("PATH")!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(paths.Home, environment.GetUserEnvironmentVariable("DOTVER_HOME"));
        var service = new SelfUninstallService(paths, environment, bootstrap);

        var outcome = await service.UninstallAsync(keepSdks: false, CancellationToken.None);

        Assert.True(outcome.Executed);
        Assert.Equal(@"C:\tools;%USERPROFILE%\bin", environment.GetUserEnvironmentVariable("PATH"));
        Assert.Null(environment.GetUserEnvironmentVariable("DOTVER_HOME"));
        var profile = Path.Combine(environment.DocumentsDirectory, "PowerShell", "Microsoft.PowerShell_profile.ps1");
        Assert.DoesNotContain("dotver", File.ReadAllText(profile), StringComparison.OrdinalIgnoreCase);
    }
}
