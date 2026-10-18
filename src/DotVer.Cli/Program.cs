using DotVer.Application;
using DotVer.Cli;
using DotVer.Domain;
using DotVer.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;

var services = new ServiceCollection()
    .AddSingleton<IAnsiConsole>(AnsiConsole.Console)
    .AddDotVer();

using var bootstrapProvider = services.BuildServiceProvider();
var bootstrap = bootstrapProvider.GetRequiredService<IEnvironmentBootstrapService>();
var registrar = new TypeRegistrar(services);

if (args.Length > 0 && string.Equals(args[0], "intercept", StringComparison.OrdinalIgnoreCase))
{
    await bootstrap.EnsureConfiguredAsync(CancellationToken.None);
    var interceptor = bootstrapProvider.GetRequiredService<IInterceptorService>();
    var interceptedArgs = args.Skip(1).ToArray();
    if (interceptedArgs.Length > 0 && interceptedArgs[0] == "--")
    {
        interceptedArgs = interceptedArgs.Skip(1).ToArray();
    }

    var result = await interceptor.ExecuteAsync(interceptedArgs, CancellationToken.None);
    if (!string.IsNullOrWhiteSpace(result.StandardOutput))
    {
        Console.Out.Write(result.StandardOutput);
    }

    if (!string.IsNullOrWhiteSpace(result.StandardError))
    {
        Console.Error.Write(result.StandardError);
    }

    return result.ExitCode;
}

var isExec = args.Length > 0 && string.Equals(args[0], "exec", StringComparison.OrdinalIgnoreCase);
var isWhich = args.Length > 0 && string.Equals(args[0], "which", StringComparison.OrdinalIgnoreCase);
var isComplete = args.Length > 0 && string.Equals(args[0], "__complete", StringComparison.OrdinalIgnoreCase);
var isCompletions = args.Length > 0 && string.Equals(args[0], "completions", StringComparison.OrdinalIgnoreCase);
var isAudit = args.Length > 0 && string.Equals(args[0], "audit", StringComparison.OrdinalIgnoreCase);
var isScan = args.Length > 0 && string.Equals(args[0], "scan", StringComparison.OrdinalIgnoreCase);
var isShell = args.Length > 0 && string.Equals(args[0], "shell", StringComparison.OrdinalIgnoreCase);
var isEnv = args.Length > 0 && string.Equals(args[0], "env", StringComparison.OrdinalIgnoreCase);
// For exec, only an immediate `exec --help` is dotver help; later --help belongs to the child.
var wantsHelp = isExec
    ? args.Length > 1 && (string.Equals(args[1], "--help", StringComparison.OrdinalIgnoreCase) || string.Equals(args[1], "-h", StringComparison.OrdinalIgnoreCase))
    : args.Contains("--help", StringComparer.OrdinalIgnoreCase) || args.Contains("-h", StringComparer.OrdinalIgnoreCase);

// exec/which/completions resolve directly and must stay side-effect free for scripts.
if (!wantsHelp && !isExec && !isWhich && !isComplete && !isCompletions && !isAudit && !isScan && !isShell && !isEnv)
{
    await bootstrap.EnsureConfiguredAsync(CancellationToken.None);
}

// Hidden data source for shell completions: plain lines, never fails the shell.
if (isComplete)
{
    try
    {
        var completionData = bootstrapProvider.GetRequiredService<ICompletionDataService>();
        var candidates = await completionData.GetCandidatesAsync(args.Length > 1 ? args[1] : string.Empty, CancellationToken.None);
        foreach (var candidate in candidates)
        {
            Console.Out.WriteLine(candidate);
        }
    }
    catch (Exception)
    {
        // Completion must produce no output (and no error) on any failure.
    }

    return 0;
}

// `exec` is parsed manually so the child's own flags are never seen by Spectre.
if (!wantsHelp && isExec)
{
    if (args.Length < 4 || !string.Equals(args[2], "--", StringComparison.Ordinal))
    {
        Console.Error.WriteLine("Usage: dotver exec <version> -- <command> [args...]");
        return 2;
    }

    try
    {
        var exec = bootstrapProvider.GetRequiredService<IExecService>();
        var result = await exec.ExecuteAsync(args[1], args.Skip(3).ToArray(), CancellationToken.None);
        return result.ExitCode;
    }
    catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or IOException or System.ComponentModel.Win32Exception)
    {
        Console.Error.WriteLine($"Error: {exception.Message}");
        return 1;
    }
}

var app = new CommandApp<DashboardCommand>(registrar);
app.Configure(config =>
{
    config.SetApplicationName("dotver");
    config.CaseSensitivity(CaseSensitivity.None);
    config.UseStrictParsing();

    config.AddCommand<DashboardCommand>("dashboard")
        .WithAlias("home")
        .WithDescription("Opens the interactive TUI experience.");

    config.AddCommand<ListCommand>("list")
        .WithAlias("ls")
        .WithDescription("Lists detected SDKs.");

    config.AddCommand<CurrentCommand>("current")
        .WithDescription("Shows the active version.");

    config.AddCommand<UseCommand>("use")
        .WithDescription("Sets the current version.");

    config.AddCommand<LocalCommand>("local")
        .WithAlias("pin")
        .WithDescription("Shows, sets or clears the per-project SDK pin in global.json.");

    config.AddCommand<DefaultCommand>("default")
        .WithDescription("Sets the default version.");

    config.AddCommand<AliasCommand>("alias")
        .WithDescription("Creates or lists aliases.");

    config.AddCommand<UnaliasCommand>("unalias")
        .WithDescription("Removes an alias.");

    config.AddCommand<EnsureCommand>("ensure")
        .WithAlias("sync")
        .WithDescription("Installs the SDK pinned by global.json if it is missing.");

    config.AddCommand<ScanCommand>("scan")
        .WithDescription("Scans a directory tree for global.json pins and reports which are satisfied. Exits non-zero if any pin is missing.");

    config.AddCommand<InstallCommand>("install")
        .WithDescription("Installs an isolated SDK using the official .NET install script.");

    config.AddCommand<AuditCommand>("audit")
        .WithDescription("Reports installed SDKs missing published security fixes (CVE). Exit 1 if vulnerable, 2 if any could not be verified.");

    config.AddCommand<OutdatedCommand>("outdated")
        .WithDescription("Shows SDKs with updates available, end-of-life channels and prereleases.");

    config.AddCommand<UpgradeCommand>("upgrade")
        .WithDescription("Upgrades managed SDKs to the latest release of their channel.");

    config.AddCommand<ListRemoteCommand>("list-remote")
        .WithAlias("ls-remote")
        .WithDescription("Queries official remote channels.");

    config.AddCommand<UninstallCommand>("uninstall")
        .WithDescription("Uninstalls a dotver-managed SDK and repairs current/default/aliases.");

    config.AddCommand<DoctorCommand>("doctor")
        .WithDescription("Runs health and readiness checks. Use --fix to repair fixable issues.");

    config.AddCommand<PruneCommand>("prune")
        .WithDescription("Shows managed SDK disk usage; --keep-latest-per-channel --yes deletes older patches. Only the current directory's global.json pin is protected.");

    config.AddCommand<RegisterRootCommand>("register-root")
        .WithDescription("Registers an additional external root for scanning.");

    config.AddCommand<ExecCommand>("exec")
        .WithDescription("Runs a command with a specific SDK: dotver exec <version> -- <command> [[args...]]");

    config.AddCommand<WhichCommand>("which")
        .WithDescription("Prints the dotnet path for a token, or for the active selection.");

    config.AddCommand<ShellCommand>("shell")
        .WithDescription("Opens an interactive shell pinned to one SDK (nothing persists after exit).");

    config.AddCommand<EnvCommand>("env")
        .WithDescription("Prints shell exports (DOTNET_ROOT, DOTVER_VERSION, PATH) for an SDK, or writes them with --github.");

    config.AddCommand<CompletionsCommand>("completions")
        .WithDescription("Prints the shell completion script for powershell, bash, zsh or fish.");

    config.AddCommand<DeactivateCommand>("deactivate")
        .WithDescription("Clears the persisted current version (falls back to default/global.json/system).");
});

return await app.RunAsync(args);
