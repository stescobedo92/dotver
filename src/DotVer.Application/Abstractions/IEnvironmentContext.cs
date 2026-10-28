namespace DotVer.Application;

public interface IEnvironmentContext
{
    string HomeDirectory { get; }
    string DocumentsDirectory { get; }
    string CurrentDirectory { get; }
    string ProcessPath { get; }
    string EntryAssemblyPath { get; }
    bool IsWindows { get; }
    bool IsLinux { get; }
    bool IsMacOS { get; }
    string RuntimeArchitecture { get; }

    /// <summary>Informational version of the running dotver build (e.g. 0.4.0), without build metadata.</summary>
    string ProductVersion { get; }
    IReadOnlyList<string> GetPathEntries();
    IReadOnlyList<string> GetKnownDotnetRoots();
    string? GetEnvironmentVariable(string name);
    string? GetUserEnvironmentVariable(string name);
    void SetUserEnvironmentVariable(string name, string value);
    void DeleteUserEnvironmentVariable(string name);
}
