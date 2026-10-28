namespace DotVer.Tests;

internal static class SymlinkSupport
{
    /// <summary>
    /// Creates a file symlink, or returns false when the runner lacks the
    /// privilege (Windows without Developer Mode / admin).
    /// </summary>
    public static bool TryCreateFile(string link, string target)
    {
        try
        {
            File.CreateSymbolicLink(link, target);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
