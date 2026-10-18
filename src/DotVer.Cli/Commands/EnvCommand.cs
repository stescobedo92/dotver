using DotVer.Application;
using DotVer.Domain;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DotVer.Cli;

internal sealed class EnvCommand : AsyncCommandBase<EnvSettings>
{
    private readonly IExecService _execService;
    private readonly ISelectionService _selectionService;
    private readonly IEnvironmentContext _environment;

    public EnvCommand(
        IAnsiConsole console,
        IExecService execService,
        ISelectionService selectionService,
        IEnvironmentContext environment)
        : base(console)
    {
        _execService = execService;
        _selectionService = selectionService;
        _environment = environment;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, EnvSettings settings)
    {
        SdkInstallation sdk;
        if (!string.IsNullOrWhiteSpace(settings.Token))
        {
            sdk = await _execService.ResolveSdkAsync(settings.Token, CancellationToken.None).ConfigureAwait(false);
        }
        else
        {
            var selection = await _selectionService.ResolveAsync(CancellationToken.None).ConfigureAwait(false);
            sdk = selection.Installation
                ?? throw new InvalidOperationException("No SDK is currently selected. Pass a token, e.g. 'dotver env 8.0'.");
        }

        if (settings.GitHub)
        {
            await WriteGitHubAsync(sdk).ConfigureAwait(false);
            return 0;
        }

        if (settings.Json)
        {
            global::System.Console.Out.WriteLine(JsonReports.ForWhich(sdk.Version.Raw, sdk.DotnetPath, sdk.HostRoot, "env"));
            return 0;
        }

        var shell = ParseShell(settings.Shell) ?? EnvScript.DetectDefault(_environment);
        global::System.Console.Out.Write(EnvScript.Render(shell, sdk));
        return 0;
    }

    private async Task WriteGitHubAsync(SdkInstallation sdk)
    {
        // GITHUB_ENV/GITHUB_PATH are line-based: a control character in the
        // path would inject extra variables or PATH entries into later steps.
        if (sdk.HostRoot.Any(char.IsControl) || sdk.Version.Raw.Any(char.IsControl))
        {
            throw new InvalidOperationException("The SDK path contains control characters and cannot be written to the GitHub Actions environment files.");
        }

        var envFile = _environment.GetEnvironmentVariable("GITHUB_ENV");
        var pathFile = _environment.GetEnvironmentVariable("GITHUB_PATH");
        if (string.IsNullOrWhiteSpace(envFile) || string.IsNullOrWhiteSpace(pathFile))
        {
            throw new InvalidOperationException("--github requires the GITHUB_ENV and GITHUB_PATH environment variables (GitHub Actions).");
        }

        await File.AppendAllTextAsync(
            envFile,
            $"DOTNET_ROOT={sdk.HostRoot}{Environment.NewLine}DOTVER_VERSION={sdk.Version.Raw}{Environment.NewLine}");
        await File.AppendAllTextAsync(pathFile, sdk.HostRoot + Environment.NewLine);
    }

    private static ShellKind? ParseShell(string? shell)
    {
        if (string.IsNullOrWhiteSpace(shell))
        {
            return null;
        }

        return shell.Trim().ToLowerInvariant() switch
        {
            "powershell" or "pwsh" => ShellKind.PowerShell,
            "bash" => ShellKind.Bash,
            "zsh" => ShellKind.Zsh,
            "fish" => ShellKind.Fish,
            "cmd" => ShellKind.Cmd,
            _ => throw new InvalidOperationException("Unknown shell. Supported shells: powershell, bash, zsh, fish, cmd.")
        };
    }
}
