namespace DotVer.Domain;

public enum PruneAction
{
    Keep,
    WouldRemove,
    Removed,
    Skipped,
    Failed
}
