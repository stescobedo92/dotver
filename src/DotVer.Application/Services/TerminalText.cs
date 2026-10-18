namespace DotVer.Application;

/// <summary>
/// Strips control characters (including ESC) from values that reach the
/// terminal. Spectre <c>Text</c> cells prevent markup parsing but still emit
/// raw ANSI escapes, and exception messages are printed verbatim, so anything
/// derived from disk names or remote metadata must pass through here first.
/// </summary>
public static class TerminalText
{
    public static string Sanitize(string? value) =>
        value is null ? string.Empty : new string(value.Where(static c => !char.IsControl(c)).ToArray());
}
