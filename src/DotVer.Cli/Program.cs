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

if (!args.Contains("--help", StringComparer.OrdinalIgnoreCase) &&
    !args.Contains("-h", StringComparer.OrdinalIgnoreCase))
{
    await bootstrap.EnsureConfiguredAsync(CancellationToken.None);
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

    config.AddCommand<DefaultCommand>("default")
        .WithDescription("Sets the default version.");

    config.AddCommand<AliasCommand>("alias")
        .WithDescription("Creates or lists aliases.");

    config.AddCommand<UnaliasCommand>("unalias")
        .WithDescription("Removes an alias.");

    config.AddCommand<InstallCommand>("install")
        .WithDescription("Installs an isolated SDK using the official .NET install script.");

    config.AddCommand<ListRemoteCommand>("list-remote")
        .WithAlias("ls-remote")
        .WithDescription("Queries official remote channels.");

    config.AddCommand<UninstallCommand>("uninstall")
        .WithDescription("Uninstalls a dotver-managed SDK and repairs current/default/aliases.");

    config.AddCommand<DoctorCommand>("doctor")
        .WithDescription("Runs health and readiness checks.");

    config.AddCommand<RegisterRootCommand>("register-root")
        .WithDescription("Registers an additional external root for scanning.");
});

return await app.RunAsync(args);
