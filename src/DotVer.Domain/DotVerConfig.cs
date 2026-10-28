namespace DotVer.Domain;

public sealed record DotVerConfig(
    string? CurrentVersion,
    string? DefaultVersion,
    Dictionary<string, string> Aliases,
    List<string> RegisteredRoots,
    MirrorConfig? Mirror = null)
{
    // A fresh instance per call: the collections are mutable, so a shared
    // singleton would leak state between callers.
    public static DotVerConfig Empty => new(null, null, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), []);
}

/// <summary>
/// Corporate mirror for downloads. Both URLs are optional and must be plain
/// https; their hosts are added to the download allow-list explicitly.
/// </summary>
public sealed record MirrorConfig(string? InstallScriptBaseUrl, string? ReleasesIndexUrl);
