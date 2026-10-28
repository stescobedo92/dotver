using System.Text.RegularExpressions;
using DotVer.Domain;

namespace DotVer.Application;

/// <summary>
/// Runs user hooks from ~/.dotver/hooks. DOTVER_HOME is the user's own
/// directory, so its scripts are trusted; they still run without a shell,
/// by absolute path, with the SDK described through environment variables.
/// </summary>
public sealed partial class HookRunner : IHookRunner
{
    private const int MaxCapturedChars = 16384;

    private readonly DotVerPaths _paths;
    private readonly IEnvironmentContext _environment;
    private readonly IProcessRunner _processRunner;

    public HookRunner(DotVerPaths paths, IEnvironmentContext environment, IProcessRunner processRunner)
    {
        _paths = paths;
        _environment = environment;
        _processRunner = processRunner;
    }

    public async Task<HookRunResult> RunAsync(string hookEvent, SdkInstallation sdk, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sdk);
        if (string.IsNullOrWhiteSpace(hookEvent) || !EventPattern().IsMatch(hookEvent))
        {
            throw new ArgumentException("Hook events are lowercase words separated by '-'.", nameof(hookEvent));
        }

        var hooksDirectory = Path.Combine(_paths.Home, "hooks");
        if (!Directory.Exists(hooksDirectory))
        {
            return new HookRunResult(hookEvent, null, Ran: false, 0, null);
        }

        var environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["DOTVER_EVENT"] = hookEvent,
            ["DOTVER_VERSION"] = sdk.Version.Raw,
            ["DOTNET_ROOT"] = sdk.HostRoot,
            ["DOTVER_SDK_ROOT"] = sdk.SdkRoot,
            ["DOTVER_HOME"] = _paths.Home
        };

        var (script, spec) = Resolve(hooksDirectory, hookEvent, environment);
        if (spec is null)
        {
            return new HookRunResult(hookEvent, null, Ran: false, 0, null);
        }

        try
        {
            var result = await _processRunner.RunAsync(spec, cancellationToken).ConfigureAwait(false);
            var output = TerminalText.Sanitize((result.StandardOutput + Environment.NewLine + result.StandardError).Trim());
            return new HookRunResult(hookEvent, script, Ran: true, result.ExitCode, output.Length == 0 ? null : output);
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or IOException)
        {
            // Typically a missing execute bit on Unix: report, never abort the install.
            return new HookRunResult(hookEvent, script, Ran: true, 126, TerminalText.Sanitize(exception.Message));
        }
    }

    private (string? Script, ProcessSpec? Spec) Resolve(string hooksDirectory, string hookEvent, IReadOnlyDictionary<string, string?> environment)
    {
        ProcessSpec Spec(string fileName, IReadOnlyList<string> arguments) =>
            new(fileName, arguments, WorkingDirectory: _environment.HomeDirectory, EnvironmentVariables: environment, MaxCapturedCharsPerStream: MaxCapturedChars);

        if (_environment.IsWindows)
        {
            var powerShellHook = Path.Combine(hooksDirectory, hookEvent + ".ps1");
            if (File.Exists(powerShellHook))
            {
                var pwsh = ExecutableLocator.FindOnPath(_environment, "pwsh.exe")
                    ?? throw new InvalidOperationException($"Hook '{TerminalText.Sanitize(powerShellHook)}' needs pwsh.exe on PATH (the current directory is intentionally excluded).");
                return (powerShellHook, Spec(pwsh, ["-NoProfile", "-NonInteractive", "-File", powerShellHook]));
            }

            foreach (var extension in new[] { ".cmd", ".bat" })
            {
                var batchHook = Path.Combine(hooksDirectory, hookEvent + extension);
                if (!File.Exists(batchHook))
                {
                    continue;
                }

                // Batch files need the command interpreter; ComSpec is the
                // system's own absolute path to it, never a PATH lookup.
                var comSpec = _environment.GetEnvironmentVariable("ComSpec");
                if (string.IsNullOrWhiteSpace(comSpec) || !Path.IsPathFullyQualified(comSpec) || !File.Exists(comSpec))
                {
                    throw new InvalidOperationException("ComSpec does not point to an existing cmd.exe; cannot run the .cmd hook.");
                }

                return (batchHook, Spec(comSpec, ["/d", "/c", batchHook]));
            }

            var executableHook = Path.Combine(hooksDirectory, hookEvent + ".exe");
            return File.Exists(executableHook) ? (executableHook, Spec(executableHook, [])) : (null, null);
        }

        foreach (var candidate in new[] { Path.Combine(hooksDirectory, hookEvent), Path.Combine(hooksDirectory, hookEvent + ".sh") })
        {
            if (File.Exists(candidate))
            {
                return (candidate, Spec(candidate, []));
            }
        }

        return (null, null);
    }

    [GeneratedRegex(@"\A[a-z][a-z-]{0,39}\z", RegexOptions.CultureInvariant)]
    private static partial Regex EventPattern();
}
