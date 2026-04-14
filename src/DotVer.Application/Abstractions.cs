using DotVer.Domain;

namespace DotVer.Application;

public interface IConfigStore
{
    Task<DotVerConfig> LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(DotVerConfig config, CancellationToken cancellationToken);
}

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
    IReadOnlyList<string> GetPathEntries();
    string? GetEnvironmentVariable(string name);
    string? GetUserEnvironmentVariable(string name);
    void SetUserEnvironmentVariable(string name, string value);
}

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken cancellationToken);
}

public interface IHttpResourceClient
{
    Task<string> GetStringAsync(string url, CancellationToken cancellationToken);
    Task DownloadFileAsync(string url, string destinationPath, CancellationToken cancellationToken);
}

public sealed record DotVerPaths(
    string Home,
    string ConfigFile,
    string ManagedInstalls,
    string Shims,
    string Cache,
    string Scripts,
    string Env)
{
    public static DotVerPaths From(IEnvironmentContext environment)
    {
        var home = environment.GetEnvironmentVariable("DOTVER_HOME");
        var root = string.IsNullOrWhiteSpace(home)
            ? Path.Combine(environment.HomeDirectory, ".dotver")
            : home;

        return new DotVerPaths(
            root,
            Path.Combine(root, "config.json"),
            Path.Combine(root, "installs"),
            Path.Combine(root, "shims"),
            Path.Combine(root, "cache"),
            Path.Combine(root, "cache", "scripts"),
            Path.Combine(root, "env"));
    }
}
