using DotVer.Application;
using DotVer.Domain;
using Spectre.Console.Testing;

namespace DotVer.Tests;

public sealed class ShimServiceTests
{
    [Fact]
    public async Task InstallsWindowsShimsAndEnvSnippets()
    {
        using var sandbox = new TestSandbox(TestPlatform.Windows);
        var environment = sandbox.CreateEnvironment();
        var service = new ShimService(DotVerPaths.From(environment), environment);

        var result = await service.InstallAsync(CancellationToken.None);

        Assert.Contains(result.FilesWritten, file => file.EndsWith("dotnet.cmd", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("dotver.ps1", service.RenderEnvSnippet(ShellKind.PowerShell), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WindowsCmdEnvSnippet_QuotesTheWholeAssignment()
    {
        using var sandbox = new TestSandbox(TestPlatform.Windows);
        var environment = sandbox.CreateEnvironment();
        var paths = DotVerPaths.From(environment);
        var service = new ShimService(paths, environment);

        await service.InstallAsync(CancellationToken.None);

        // `set X=value` leaves & | < > in the path live for cmd.exe; the
        // quoted form `set "X=value"` is the only safe spelling.
        var cmd = File.ReadAllText(Path.Combine(paths.Env, "dotver.cmd"));
        Assert.Contains("set \"PATH=" + paths.Shims + ";%PATH%\"", cmd, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(TestPlatform.Linux)]
    [InlineData(TestPlatform.MacOS)]
    public async Task InstallsUnixShimsAndEnvSnippets(TestPlatform platform)
    {
        using var sandbox = new TestSandbox(platform);
        var environment = sandbox.CreateEnvironment();
        var paths = DotVerPaths.From(environment);
        var service = new ShimService(paths, environment);

        var result = await service.InstallAsync(CancellationToken.None);

        var shimPath = Path.Combine(paths.Shims, "dotnet");
        Assert.Contains(result.FilesWritten, file => file.EndsWith($"{Path.DirectorySeparatorChar}dotnet", StringComparison.Ordinal));
        Assert.True(File.Exists(shimPath));
        Assert.Contains("dotver.sh", service.RenderEnvSnippet(ShellKind.Bash), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("dotver.fish", service.RenderEnvSnippet(ShellKind.Fish), StringComparison.OrdinalIgnoreCase);

        if (!OperatingSystem.IsWindows())
        {
            var mode = File.GetUnixFileMode(shimPath);
            Assert.True(mode.HasFlag(UnixFileMode.UserExecute));
        }
    }

    [Fact]
    public async Task UnixShim_EscapesShellMetacharactersInProcessPath()
    {
        using var sandbox = new TestSandbox(TestPlatform.Linux);
        var home = Path.Combine(sandbox.Root, "home");
        Directory.CreateDirectory(home);
        var variables = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["DOTVER_HOME"] = Path.Combine(sandbox.Root, ".dotver"),
            ["PATH"] = string.Empty
        };
        var evilProcess = Path.Combine(sandbox.Root, "evil$(touch-pwned)", "dotnet");
        var environment = new TestEnvironmentContext(home, sandbox.Root, evilProcess, variables, TestPlatform.Linux);
        var paths = DotVerPaths.From(environment);
        var service = new ShimService(paths, environment);

        await service.InstallAsync(CancellationToken.None);

        var shim = File.ReadAllText(Path.Combine(paths.Shims, "dotnet"));
        Assert.Contains("\\$(touch-pwned)", shim, StringComparison.Ordinal);
        Assert.DoesNotContain("\"" + evilProcess + "\"", shim, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WindowsBootstrapUpdatesPowerShellProfiles()
    {
        using var sandbox = new TestSandbox(TestPlatform.Windows);
        var environment = sandbox.CreateEnvironment();
        var paths = DotVerPaths.From(environment);
        var shimService = new ShimService(paths, environment);
        var bootstrap = new EnvironmentBootstrapService(paths, environment, shimService);

        var result = await bootstrap.EnsureConfiguredAsync(CancellationToken.None);

        Assert.Contains(result.ProfilesUpdated, profile => profile.EndsWith("Microsoft.PowerShell_profile.ps1", StringComparison.OrdinalIgnoreCase));
        var pwshProfile = Path.Combine(environment.DocumentsDirectory, "PowerShell", "Microsoft.PowerShell_profile.ps1");
        Assert.True(File.Exists(pwshProfile));
        Assert.Contains("dotver.ps1", File.ReadAllText(pwshProfile), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(TestPlatform.Linux)]
    [InlineData(TestPlatform.MacOS)]
    public async Task UnixBootstrapUpdatesShellProfiles(TestPlatform platform)
    {
        using var sandbox = new TestSandbox(platform);
        var environment = sandbox.CreateEnvironment();
        Directory.CreateDirectory(Path.Combine(environment.HomeDirectory, ".config", "fish"));
        var paths = DotVerPaths.From(environment);
        var shimService = new ShimService(paths, environment);
        var bootstrap = new EnvironmentBootstrapService(paths, environment, shimService);

        var result = await bootstrap.EnsureConfiguredAsync(CancellationToken.None);

        Assert.Contains(result.ProfilesUpdated, profile => profile.EndsWith(".profile", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.ProfilesUpdated, profile => profile.EndsWith(".bashrc", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.ProfilesUpdated, profile => profile.EndsWith(".zshrc", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.ProfilesUpdated, profile => profile.EndsWith("config.fish", StringComparison.OrdinalIgnoreCase));

        Assert.Contains("dotver.sh", File.ReadAllText(Path.Combine(environment.HomeDirectory, ".profile")), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("dotver.sh", File.ReadAllText(Path.Combine(environment.HomeDirectory, ".bashrc")), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("dotver.sh", File.ReadAllText(Path.Combine(environment.HomeDirectory, ".zshrc")), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("dotver.fish", File.ReadAllText(Path.Combine(environment.HomeDirectory, ".config", "fish", "config.fish")), StringComparison.OrdinalIgnoreCase);
    }
}
