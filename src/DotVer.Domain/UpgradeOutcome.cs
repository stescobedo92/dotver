namespace DotVer.Domain;

public sealed record UpgradeOutcome(
    string Channel,
    string FromVersion,
    string ToVersion,
    bool Upgraded,
    bool RepointedCurrent,
    bool RepointedDefault,
    IReadOnlyList<string> RepointedAliases,
    bool PrunedOld,
    string? Error = null,
    string? Note = null);
