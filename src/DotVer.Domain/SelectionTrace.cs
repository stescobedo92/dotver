namespace DotVer.Domain;

/// <summary>One link of the selection chain, in evaluation order.</summary>
/// <param name="Source">DOTVER_VERSION, global.json, current, default or system.</param>
/// <param name="Value">What the source contributed (a token, a file path, a host path), if anything.</param>
/// <param name="Outcome">One of <see cref="SelectionOutcomes"/>.</param>
/// <param name="Detail">Human-readable reason for the outcome.</param>
/// <param name="Winner">True for the single step that produced the selection.</param>
public sealed record SelectionStep(string Source, string? Value, string Outcome, string Detail, bool Winner);

public static class SelectionOutcomes
{
    public const string Selected = "selected";
    public const string Skipped = "skipped";
    public const string Unmatched = "unmatched";
    public const string Overridden = "overridden";
    public const string Error = "error";
}

/// <summary>
/// The full selection chain. <see cref="Error"/> is set when DOTVER_VERSION names
/// a version that is not installed: resolution fails loudly, but the chain is
/// still explained so the user can see what would win once it is fixed.
/// </summary>
public sealed record SelectionTrace(IReadOnlyList<SelectionStep> Steps, SdkSelection Selection, string? Error);
