using DotVer.Application;

namespace DotVer.Tests;

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
    public string RuntimeArchitecture => "x64";

    public IReadOnlyList<string> GetPathEntries()
    {
        if (!_variables.TryGetValue("PATH", out var value) || string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        return value.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public IReadOnlyList<string> GetKnownDotnetRoots()
    {
        if (!IsWindows)
        {
            // Sandbox-contained stand-in: tests must never discover the host's real /usr/share/dotnet.
            return [Path.Combine(CurrentDirectory, "known-dotnet-root")];
        }

        var roots = new List<string>();
        var programFiles = GetEnvironmentVariable("ProgramFiles");
        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            roots.Add(Path.Combine(programFiles, "dotnet"));
        }

        var programFilesX86 = GetEnvironmentVariable("ProgramFiles(x86)");
        if (!string.IsNullOrWhiteSpace(programFilesX86))
        {
            roots.Add(Path.Combine(programFilesX86, "dotnet"));
        }

        return roots;
    }

    public string? GetEnvironmentVariable(string name) => _variables.TryGetValue(name, out var value) ? value : null;

    public string? GetUserEnvironmentVariable(string name) => _variables.TryGetValue(name, out var value) ? value : null;

    public void SetUserEnvironmentVariable(string name, string value) => _variables[name] = value;
}
