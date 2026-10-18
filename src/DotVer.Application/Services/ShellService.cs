using DotVer.Domain;

namespace DotVer.Application;

public sealed class ShellService : IShellService
{
    private readonly IExecService _execService;
    private readonly IProcessRunner _processRunner;
    private readonly IEnvironmentContext _environment;

    public ShellService(IExecService execService, IProcessRunner processRunner, IEnvironmentContext environment)
    {
        _execService = execService;
        _processRunner = processRunner;
        _environment = environment;
    }

    public async Task<ProcessResult> SpawnShellAsync(string token, string? shellOverride, CancellationToken cancellationToken)
    {
        var sdk = await _execService.ResolveSdkAsync(token, cancellationToken).ConfigureAwait(false);
        var shell = ResolveShell(shellOverride);
        var environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["DOTNET_ROOT"] = sdk.HostRoot,
            ["DOTVER_VERSION"] = sdk.Version.Raw,
            ["DOTVER_SHELL"] = sdk.Version.Raw
        };

        var spec = new ProcessSpec(
            shell,
            InteractiveArgs(shell),
            WorkingDirectory: _environment.CurrentDirectory,
            EnvironmentVariables: environment,
            InheritStandardIo: true);

        return await _processRunner.RunAsync(spec, cancellationToken).ConfigureAwait(false);
    }

    private string ResolveShell(string? shellOverride)
    {
        if (!string.IsNullOrWhiteSpace(shellOverride))
        {
            var hasDirectory = shellOverride.Contains(Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
                               shellOverride.Contains(Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
            if (hasDirectory)
            {
                // Explicit shell paths must be absolute: a relative override
                // would bind execution to the (possibly untrusted) cwd.
                if (!Path.IsPathFullyQualified(shellOverride))
                {
                    throw new InvalidOperationException("A shell override with a directory must be an absolute path.");
                }

                return File.Exists(shellOverride)
                    ? Path.GetFullPath(shellOverride)
                    : throw new InvalidOperationException($"Shell '{Sanitize(shellOverride)}' does not exist.");
            }

            return FindOnPath(shellOverride)
                ?? throw new InvalidOperationException($"Shell '{Sanitize(shellOverride)}' was not found on PATH (the current directory is intentionally excluded).");
        }

        if (_environment.IsWindows)
        {
            return FindOnPath("pwsh") ?? FindOnPath("powershell")
                ?? throw new InvalidOperationException("Could not find pwsh or powershell on PATH.");
        }

        var fromEnvironment = _environment.GetEnvironmentVariable("SHELL");
        if (!string.IsNullOrWhiteSpace(fromEnvironment) &&
            Path.IsPathFullyQualified(fromEnvironment) &&
            File.Exists(fromEnvironment))
        {
            return fromEnvironment;
        }

        return FindOnPath("bash") ?? FindOnPath("zsh") ?? FindOnPath("sh")
            ?? throw new InvalidOperationException("Could not find a shell on PATH (bash/zsh/sh).");
    }

    private string? FindOnPath(string name)
    {
        var bare = Path.GetFileName(name);
        var found = ExecutableLocator.FindOnPath(_environment, bare);
        if (found is null && _environment.IsWindows && !bare.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            found = ExecutableLocator.FindOnPath(_environment, bare + ".exe");
        }

        return found;
    }

    private static IReadOnlyList<string> InteractiveArgs(string shellPath)
    {
        var name = Path.GetFileNameWithoutExtension(shellPath).ToLowerInvariant();
        return name switch
        {
            "pwsh" or "powershell" => ["-NoLogo"],
            "cmd" => [],
            _ => ["-i"]
        };
    }

    private static string Sanitize(string value) =>
        new(value.Where(static c => !char.IsControl(c)).ToArray());
}
