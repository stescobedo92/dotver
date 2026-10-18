using DotVer.Domain;

namespace DotVer.Application;

/// <summary>
/// Renders environment exports for a resolved SDK. Paths are emitted as
/// single-quoted literals in every shell dialect so metacharacters in a
/// directory name can never become code when the output is sourced.
/// </summary>
public static class EnvScript
{
    public static string Render(ShellKind shell, SdkInstallation sdk)
    {
        ArgumentNullException.ThrowIfNull(sdk);
        var root = sdk.HostRoot;
        var version = sdk.Version.Raw;

        // Sourced output must stay single-line safe in every dialect: a
        // control character in a path would break out of the assignment.
        if (root.Any(char.IsControl) || version.Any(char.IsControl) ||
            shell == ShellKind.Cmd && (root.Contains('!') || version.Contains('!')))
        {
            throw new ArgumentException("The SDK path or version contains characters that cannot be exported safely to a shell script.", nameof(sdk));
        }

        return shell switch
        {
            ShellKind.PowerShell =>
                $"$env:DOTNET_ROOT = {PowerShellSingleQuoted(root)}{Environment.NewLine}" +
                $"$env:DOTVER_VERSION = {PowerShellSingleQuoted(version)}{Environment.NewLine}" +
                $"$env:PATH = {PowerShellSingleQuoted(root + ";")} + $env:PATH{Environment.NewLine}",
            ShellKind.Bash or ShellKind.Zsh =>
                $"export DOTNET_ROOT={PosixSingleQuoted(root)}{Environment.NewLine}" +
                $"export DOTVER_VERSION={PosixSingleQuoted(version)}{Environment.NewLine}" +
                $"export PATH={PosixSingleQuoted(root + ":")}\"$PATH\"{Environment.NewLine}",
            ShellKind.Fish =>
                $"set -gx DOTNET_ROOT {FishSingleQuoted(root)}{Environment.NewLine}" +
                $"set -gx DOTVER_VERSION {FishSingleQuoted(version)}{Environment.NewLine}" +
                $"set -gx PATH {FishSingleQuoted(root)} $PATH{Environment.NewLine}",
            ShellKind.Cmd =>
                $"set \"DOTNET_ROOT={CmdBatchValue(root)}\"{Environment.NewLine}" +
                $"set \"DOTVER_VERSION={CmdBatchValue(version)}\"{Environment.NewLine}" +
                $"set \"PATH={CmdBatchValue(root)};%PATH%\"{Environment.NewLine}",
            _ => throw new ArgumentOutOfRangeException(nameof(shell), shell, "Unsupported shell.")
        };
    }

    public static ShellKind DetectDefault(IEnvironmentContext environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        if (environment.IsWindows)
        {
            return ShellKind.PowerShell;
        }

        var shell = environment.GetEnvironmentVariable("SHELL");
        var name = string.IsNullOrWhiteSpace(shell) ? string.Empty : Path.GetFileName(shell).ToLowerInvariant();
        return name switch
        {
            "zsh" => ShellKind.Zsh,
            "fish" => ShellKind.Fish,
            "pwsh" or "powershell" => ShellKind.PowerShell,
            _ => ShellKind.Bash
        };
    }

    private static string PowerShellSingleQuoted(string value) =>
        "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    private static string PosixSingleQuoted(string value) =>
        "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    private static string FishSingleQuoted(string value) =>
        "'" + value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("'", "\\'", StringComparison.Ordinal) + "'";

    // Batch-file values: % must be doubled or cmd expands it before set runs.
    private static string CmdBatchValue(string value) =>
        value
            .Replace("\"", string.Empty, StringComparison.Ordinal)
            .Replace("%", "%%", StringComparison.Ordinal);
}
