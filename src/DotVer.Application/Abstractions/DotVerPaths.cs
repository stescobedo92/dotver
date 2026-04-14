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
