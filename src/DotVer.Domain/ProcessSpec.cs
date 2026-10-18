namespace DotVer.Domain;

public sealed record ProcessSpec(
    string FileName,
    IReadOnlyList<string> Arguments,
    string? WorkingDirectory = null,
    IReadOnlyDictionary<string, string?>? EnvironmentVariables = null,
    bool InheritStandardIo = false,
    int? MaxCapturedCharsPerStream = null);
