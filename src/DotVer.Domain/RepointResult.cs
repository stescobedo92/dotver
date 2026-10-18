namespace DotVer.Domain;

public sealed record RepointResult(bool RepointedCurrent, bool RepointedDefault, IReadOnlyList<string> RepointedAliases);
