using System.Reflection;
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

    public string EntryAssemblyPath => Assembly.GetEntryAssembly()?.Location ?? ProcessPath;

    public bool IsWindows => OperatingSystem.IsWindows();

    public bool IsLinux => OperatingSystem.IsLinux();

    public bool IsMacOS => OperatingSystem.IsMacOS();

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

    public string? GetEnvironmentVariable(string name) => Environment.GetEnvironmentVariable(name);

    public string? GetUserEnvironmentVariable(string name) =>
        Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User);

    public void SetUserEnvironmentVariable(string name, string value) =>
        Environment.SetEnvironmentVariable(name, value, EnvironmentVariableTarget.User);
}
