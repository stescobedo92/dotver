using DotVer.Application;
using DotVer.Domain;

namespace DotVer.Tests;

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
