using System.Runtime.InteropServices;
using DotVer.Application;

namespace DotVer.Infrastructure;

public sealed class SystemEnvironmentContext : IEnvironmentContext
{
    public string HomeDirectory =>
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public string DocumentsDirectory =>
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    public string CurrentDirectory => Directory.GetCurrentDirectory();

    public string ProcessPath => Environment.ProcessPath ?? "dotver";

    // Assembly.Location is empty (and IL3000) in single-file apps. The first
    // command-line arg is the managed dll when launched as `dotnet dotver.dll`
    // and the executable path otherwise, which is all ShimService needs.
    public string EntryAssemblyPath
    {
        get
        {
            var args = Environment.GetCommandLineArgs();
            if (args.Length == 0 || string.IsNullOrWhiteSpace(args[0]))
            {
                return ProcessPath;
            }

            try
            {
                var fullPath = Path.GetFullPath(args[0]);
                return File.Exists(fullPath) ? fullPath : ProcessPath;
            }
            catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
            {
                return ProcessPath;
            }
        }
    }

    public bool IsWindows => OperatingSystem.IsWindows();

    public bool IsLinux => OperatingSystem.IsLinux();

    public bool IsMacOS => OperatingSystem.IsMacOS();

    public string RuntimeArchitecture => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.X64 => "x64",
        Architecture.X86 => "x86",
        Architecture.Arm64 => "arm64",
        Architecture.Arm => "arm",
        Architecture.S390x => "s390x",
        Architecture.Ppc64le => "ppc64le",
        Architecture.LoongArch64 => "loongarch64",
        Architecture.RiscV64 => "riscv64",
        _ => RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()
    };

    public IReadOnlyList<string> GetPathEntries()
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return [];
        }

        return path
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(IsWindows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
            .ToList();
    }

    public IReadOnlyList<string> GetKnownDotnetRoots()
    {
        if (!IsWindows)
        {
            return ["/usr/share/dotnet", "/usr/local/share/dotnet", "/usr/lib/dotnet"];
        }

        var roots = new List<string>();
        var programFiles = Environment.GetEnvironmentVariable("ProgramFiles");
        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            roots.Add(Path.Combine(programFiles, "dotnet"));
        }

        var programFilesX86 = Environment.GetEnvironmentVariable("ProgramFiles(x86)");
        if (!string.IsNullOrWhiteSpace(programFilesX86))
        {
            roots.Add(Path.Combine(programFilesX86, "dotnet"));
        }

        return roots;
    }

    public string? GetEnvironmentVariable(string name) => Environment.GetEnvironmentVariable(name);

    public string? GetUserEnvironmentVariable(string name) =>
        Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User);

    public void SetUserEnvironmentVariable(string name, string value) =>
        Environment.SetEnvironmentVariable(name, value, EnvironmentVariableTarget.User);
}
