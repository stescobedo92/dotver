namespace DotVer.Domain;

/// <summary>
/// One token of a multi-version <c>dotver install a b c</c>. A token that
/// resolves to a version another token already covered is reported as a
/// duplicate instead of being installed twice.
/// </summary>
public sealed record MultiInstallEntry(
    string RequestedToken,
    InstallPlan? Plan,
    InstallOutcome? Outcome,
    string? Error,
    bool Duplicate = false)
{
    public bool Succeeded => Outcome is not null && Error is null;
}

public sealed record MultiInstallResult(IReadOnlyList<MultiInstallEntry> Entries)
{
    public bool AllSucceeded => Entries.All(static entry => entry.Error is null);

    public int Failed => Entries.Count(static entry => entry.Error is not null);

    /// <summary>Outcomes of the tokens that produced an installation, in request order.</summary>
    public IReadOnlyList<InstallOutcome> Outcomes =>
        Entries.Where(static entry => entry.Outcome is not null).Select(static entry => entry.Outcome!).ToList();
}
