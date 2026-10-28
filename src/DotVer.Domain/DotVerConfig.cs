namespace DotVer.Domain;

public sealed record DotVerConfig(
    string? CurrentVersion,
    string? DefaultVersion,
    Dictionary<string, string> Aliases,
    List<string> RegisteredRoots)
{
    // A fresh instance per call: the collections are mutable, so a shared
    // singleton would leak state between callers.
    public static DotVerConfig Empty => new(null, null, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), []);
}
