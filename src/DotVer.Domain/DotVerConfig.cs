namespace DotVer.Domain;

public sealed record DotVerConfig(
    string? CurrentVersion,
    string? DefaultVersion,
    Dictionary<string, string> Aliases,
    List<string> RegisteredRoots)
{
    public static DotVerConfig Empty { get; } = new(null, null, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), []);
}
