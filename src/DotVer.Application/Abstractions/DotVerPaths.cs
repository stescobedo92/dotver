namespace DotVer.Application;

public sealed record DotVerPaths(
    string Home,
    string ConfigFile,
    string ManagedInstalls,
    string Shims,
    string Cache,
    string Scripts,
    string Env)
{
    public string ManagedRuntimes => Path.Combine(Home, "runtimes");
    public static DotVerPaths From(IEnvironmentContext environment)
    {
        var home = environment.GetEnvironmentVariable("DOTVER_HOME");

        // Anchor once: inventory ignores relative root hints, so a relative
        // DOTVER_HOME must become absolute here rather than drift with the cwd.
        var root = Path.GetFullPath(string.IsNullOrWhiteSpace(home)
            ? Path.Combine(environment.HomeDirectory, ".dotver")
            : home.Trim());

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
