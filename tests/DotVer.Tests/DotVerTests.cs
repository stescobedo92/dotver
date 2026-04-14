using DotVer.Application;
using DotVer.Domain;
using Spectre.Console.Testing;

namespace DotVer.Tests;

public sealed class GlobalJsonServiceTests
{
    [Fact]
    public void FindsGlobalJsonWalkingUpDirectoryTree()
    {
        using var sandbox = new TestSandbox();
        var repo = sandbox.CreateDirectory("repo");
        var src = Directory.CreateDirectory(Path.Combine(repo, "src", "app"));
        File.WriteAllText(
            Path.Combine(repo, "global.json"),
            """
            {
              "sdk": {
                "version": "8.0.412",
                "rollForward": "feature"
              }
            }
            """);

        var service = new GlobalJsonService();

        var request = service.TryFind(src.FullName);

        Assert.NotNull(request);
        Assert.Equal("8.0.412", request!.Version.Raw);
        Assert.Equal(SdkRollForward.Feature, request.RollForward);
    }
}

public sealed class VersionResolverTests
{
    [Fact]
    public void ResolvesAliasAndLatestAgainstInstalledInventory()
    {
        var config = new DotVerConfig(
            null,
            null,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["lts"] = "8.0.412"
            },
            []);

        var installations = new[]
        {
            MakeInstallation("8.0.412", true),
            MakeInstallation("10.0.201", true)
        };

        var resolver = new VersionResolver();

        Assert.Equal("8.0.412", resolver.ResolveToken("lts", config, installations));
        Assert.Equal("10.0.201", resolver.ResolveToken("latest", config, installations));
    }

    [Fact]
    public void ResolvesExternalSdkForUse()
    {
        var config = DotVerConfig.Empty;
        var installations = new[]
        {
            MakeInstallation("6.0.428", false, canBeUsedDirectly: false),
            MakeInstallation("8.0.412", true)
        };

        var resolver = new VersionResolver();
        var sdk = resolver.FindByToken("6.0.428", config, installations);

        Assert.NotNull(sdk);
        Assert.Equal("6.0.428", sdk!.Version.Raw);
    }

    private static SdkInstallation MakeInstallation(string version, bool isManaged, bool canBeUsedDirectly = true) =>
        new(SdkVersion.Parse(version), $"/fake/{version}", $"/fake/{version}/sdk/{version}", $"/fake/{version}/dotnet", isManaged ? SdkOriginKind.ManagedInstall : SdkOriginKind.OfficialInstallRoot, "test", isManaged, canBeUsedDirectly, "test");
}

public sealed class SelectionAndInterceptTests
{
    [Fact]
    public async Task CurrentVersionWinsOverGlobalJson()
    {
        using var sandbox = new TestSandbox(TestPlatform.Windows);
        var managedInstall = sandbox.CreateSdkInstall("8.0.412", isManaged: true);
        var officialInstall = sandbox.CreateSdkInstall("10.0.201", isManaged: false, rootName: "system-dotnet");
        File.WriteAllText(
            Path.Combine(sandbox.Root, "global.json"),
            """
            {
              "sdk": {
                "version": "10.0.201"
              }
            }
            """);

        var config = new InMemoryConfigStore(new DotVerConfig("8.0.412", null, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), []));
        var environment = sandbox.CreateEnvironment();
        var inventory = new InventoryService(DotVerPaths.From(environment), environment, config);
        var selectionService = new SelectionService(inventory, config, new GlobalJsonService(), new VersionResolver(), environment);

        var selection = await selectionService.ResolveAsync(CancellationToken.None);

        Assert.NotNull(selection.Installation);
        Assert.Equal(managedInstall.Version.Raw, selection.Installation!.Version.Raw);
        Assert.DoesNotContain(officialInstall.Version.Raw, selection.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InterceptorHandlesVersionAndListSdksWithoutCallingRealDotnet()
    {
        using var sandbox = new TestSandbox(TestPlatform.Windows);
        sandbox.CreateSdkInstall("8.0.412", isManaged: true);
        sandbox.CreateSdkInstall("10.0.201", isManaged: false, rootName: "system-dotnet");

        var config = new InMemoryConfigStore(new DotVerConfig("8.0.412", null, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), []));
        var environment = sandbox.CreateEnvironment();
        var inventory = new InventoryService(DotVerPaths.From(environment), environment, config);
        var selectionService = new SelectionService(inventory, config, new GlobalJsonService(), new VersionResolver(), environment);
        var processRunner = new FakeProcessRunner();
        var interceptor = new InterceptorService(selectionService, inventory, processRunner);

        var versionResult = await interceptor.ExecuteAsync(["--version"], CancellationToken.None);
        var listResult = await interceptor.ExecuteAsync(["--list-sdks"], CancellationToken.None);

        Assert.Equal("8.0.412" + Environment.NewLine, versionResult.StandardOutput);
        Assert.Contains("10.0.201", listResult.StandardOutput, StringComparison.Ordinal);
        Assert.Empty(processRunner.Invocations);
    }
}

public sealed class StateServiceTests
{
    [Fact]
    public async Task UninstallManagedSdkRepairsCurrentDefaultAndAliases()
    {
        using var sandbox = new TestSandbox(TestPlatform.Windows);
        sandbox.CreateSdkInstall("8.0.412", isManaged: true);
        sandbox.CreateSdkInstall("7.0.410", isManaged: true);

        var configStore = new InMemoryConfigStore(new DotVerConfig(
            "8.0.412",
            "8.0.412",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["lts"] = "8.0.412"
            },
            []));
        var environment = sandbox.CreateEnvironment();
        var inventory = new InventoryService(DotVerPaths.From(environment), environment, configStore);
        var stateService = new StateService(configStore, new VersionResolver(), inventory, DotVerPaths.From(environment));

        var outcome = await stateService.UninstallManagedAsync("8.0.412", CancellationToken.None);

        Assert.Equal("8.0.412", outcome.RemovedVersion);
        Assert.Equal("7.0.410", outcome.NewCurrentVersion);
        Assert.Equal("7.0.410", outcome.NewDefaultVersion);
        Assert.Contains("lts", outcome.RemovedAliases);
    }
}

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

internal sealed class InMemoryConfigStore : IConfigStore
{
    private DotVerConfig _config;

    public InMemoryConfigStore(DotVerConfig config)
    {
        _config = config;
    }

    public Task<DotVerConfig> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(_config);

    public Task SaveAsync(DotVerConfig config, CancellationToken cancellationToken)
    {
        _config = config;
        return Task.CompletedTask;
    }
}

internal sealed class FakeProcessRunner : IProcessRunner
{
    public List<ProcessSpec> Invocations { get; } = [];

    public Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken cancellationToken)
    {
        Invocations.Add(spec);
        return Task.FromResult(new ProcessResult(0, "fake-output", string.Empty));
    }
}

internal sealed class TestEnvironmentContext : IEnvironmentContext
{
    private readonly Dictionary<string, string?> _variables;
    private readonly TestPlatform _platform;

    public TestEnvironmentContext(string homeDirectory, string currentDirectory, string processPath, Dictionary<string, string?> variables, TestPlatform platform)
    {
        HomeDirectory = homeDirectory;
        CurrentDirectory = currentDirectory;
        ProcessPath = processPath;
        EntryAssemblyPath = Path.Combine(currentDirectory, "dotver.dll");
        _variables = variables;
        _platform = platform;
    }

    public string HomeDirectory { get; }
    public string DocumentsDirectory => Path.Combine(HomeDirectory, "Documents");
    public string CurrentDirectory { get; }
    public string ProcessPath { get; }
    public string EntryAssemblyPath { get; }
    public bool IsWindows => _platform == TestPlatform.Windows;
    public bool IsLinux => _platform == TestPlatform.Linux;
    public bool IsMacOS => _platform == TestPlatform.MacOS;

    public IReadOnlyList<string> GetPathEntries()
    {
        if (!_variables.TryGetValue("PATH", out var value) || string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        return value.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public string? GetEnvironmentVariable(string name) => _variables.TryGetValue(name, out var value) ? value : null;

    public string? GetUserEnvironmentVariable(string name) => _variables.TryGetValue(name, out var value) ? value : null;

    public void SetUserEnvironmentVariable(string name, string value) => _variables[name] = value;
}

internal sealed class TestSandbox : IDisposable
{
    private readonly string _root;
    private readonly TestPlatform _platform;

    public TestSandbox(TestPlatform platform = TestPlatform.Linux)
    {
        _root = Path.Combine(Path.GetTempPath(), "dotver-tests", Guid.NewGuid().ToString("N"));
        _platform = platform;
        Directory.CreateDirectory(_root);
    }

    public string Root => _root;

    public string CreateDirectory(string name)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    public SdkInstallation CreateSdkInstall(string version, bool isManaged, string? rootName = null)
    {
        var root = isManaged
            ? Path.Combine(_root, ".dotver", "installs", version)
            : Path.Combine(_root, rootName ?? "system-dotnet");
        Directory.CreateDirectory(Path.Combine(root, "sdk", version));
        var dotnetFile = Path.Combine(root, _platform == TestPlatform.Windows ? "dotnet.exe" : "dotnet");
        File.WriteAllText(dotnetFile, string.Empty);

        return new SdkInstallation(
            SdkVersion.Parse(version),
            root,
            Path.Combine(root, "sdk", version),
            dotnetFile,
            isManaged ? SdkOriginKind.ManagedInstall : SdkOriginKind.OfficialInstallRoot,
            "test",
            isManaged,
            true,
            "test");
    }

    public TestEnvironmentContext CreateEnvironment()
    {
        var home = Path.Combine(_root, "home");
        Directory.CreateDirectory(home);
        var globalJsonPath = Path.Combine(_root, "global.json");
        if (!File.Exists(globalJsonPath))
        {
            File.WriteAllText(globalJsonPath, "{}");
        }

        var programFiles = Path.Combine(_root, "programfiles");
        Directory.CreateDirectory(programFiles);

        var systemDotnet = Path.Combine(_root, "system-dotnet");
        if (Directory.Exists(systemDotnet))
        {
            var target = Path.Combine(programFiles, "dotnet");
            if (!Directory.Exists(target))
            {
                CopyDirectory(systemDotnet, target);
            }
        }

        var variables = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["DOTVER_HOME"] = Path.Combine(_root, ".dotver"),
            ["PATH"] = string.Empty,
            ["ProgramFiles"] = programFiles
        };

        var processName = _platform == TestPlatform.Windows ? "dotver.exe" : "dotver";
        return new TestEnvironmentContext(home, _root, Path.Combine(_root, processName), variables, _platform);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(directory.Replace(source, destination, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, file.Replace(source, destination, StringComparison.OrdinalIgnoreCase), overwrite: true);
        }
    }
}

public enum TestPlatform
{
    Windows,
    Linux,
    MacOS
}
