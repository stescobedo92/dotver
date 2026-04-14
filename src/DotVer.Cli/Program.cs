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
var bootstrap = bootstrapProvider.GetRequiredService<EnvironmentBootstrapService>();
var registrar = new TypeRegistrar(services);

if (args.Length > 0 && string.Equals(args[0], "intercept", StringComparison.OrdinalIgnoreCase))
{
    await bootstrap.EnsureConfiguredAsync(CancellationToken.None);
    var interceptor = bootstrapProvider.GetRequiredService<InterceptorService>();
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
        .WithDescription("Abre la experiencia TUI interactiva.");

    config.AddCommand<ListCommand>("list")
        .WithAlias("ls")
        .WithDescription("Lista SDKs detectados.");

    config.AddCommand<CurrentCommand>("current")
        .WithDescription("Muestra la version activa.");

    config.AddCommand<UseCommand>("use")
        .WithDescription("Selecciona la version actual.");

    config.AddCommand<DefaultCommand>("default")
        .WithDescription("Define la version por defecto.");

    config.AddCommand<AliasCommand>("alias")
        .WithDescription("Crea o lista aliases.");

    config.AddCommand<UnaliasCommand>("unalias")
        .WithDescription("Elimina un alias.");

    config.AddCommand<InstallCommand>("install")
        .WithDescription("Instala un SDK aislado usando el script oficial de .NET.");

    config.AddCommand<ListRemoteCommand>("list-remote")
        .WithAlias("ls-remote")
        .WithDescription("Consulta canales remotos oficiales.");

    config.AddCommand<UninstallCommand>("uninstall")
        .WithDescription("Desinstala un SDK managed por dotver y repara current/default/aliases.");

    config.AddCommand<DoctorCommand>("doctor")
        .WithDescription("Ejecuta checks de salud y preparacion.");

    config.AddCommand<RegisterRootCommand>("register-root")
        .WithDescription("Registra una raiz externa adicional para escaneo.");
});

return await app.RunAsync(args);
