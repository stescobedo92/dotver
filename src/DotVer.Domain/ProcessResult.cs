namespace DotVer.Domain;

public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
