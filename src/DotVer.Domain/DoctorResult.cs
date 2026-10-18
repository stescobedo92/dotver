namespace DotVer.Domain;

public sealed record DoctorIssue(string Code, string Message);

public sealed record DoctorResult(IReadOnlyList<DoctorIssue> Issues, IReadOnlyList<string> FixesApplied);
