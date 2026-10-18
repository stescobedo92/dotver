using DotVer.Application;
using Microsoft.Extensions.DependencyInjection;

namespace DotVer.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDotVer(this IServiceCollection services)
    {
        services.AddSingleton<IEnvironmentContext, SystemEnvironmentContext>();
        services.AddSingleton(static provider => DotVerPaths.From(provider.GetRequiredService<IEnvironmentContext>()));
        services.AddSingleton<IConfigStore, JsonConfigStore>();
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddHttpClient<IHttpResourceClient, HttpResourceClient>();

        services.AddSingleton<IGlobalJsonService, GlobalJsonService>();
        services.AddSingleton<IVersionResolver, VersionResolver>();
        services.AddSingleton<IInventoryService, InventoryService>();
        services.AddSingleton<ISelectionService, SelectionService>();
        services.AddSingleton<IInterceptorService, InterceptorService>();
        services.AddSingleton<IShimService, ShimService>();
        services.AddSingleton<IEnvironmentBootstrapService, EnvironmentBootstrapService>();
        services.AddSingleton<IInstallService, InstallService>();
        services.AddSingleton<IStateService, StateService>();
        services.AddSingleton<IEnsureService, EnsureService>();
        services.AddSingleton<IExecService, ExecService>();
        services.AddSingleton<IOutdatedService, OutdatedService>();
        services.AddSingleton<ICompletionDataService, CompletionDataService>();
        services.AddSingleton<IDoctorService, DoctorService>();
        services.AddSingleton<IPruneService, PruneService>();
        services.AddSingleton<IAuditService, AuditService>();
        services.AddSingleton<IWorkspaceService, WorkspaceService>();
        services.AddSingleton<IShellService, ShellService>();

        // Dedicated metadata client: redirects are never followed, so a
        // poisoned endpoint cannot bounce requests to internal services.
        services.AddHttpClient<IReleaseMetadataService, ReleaseMetadataService>(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(30);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AllowAutoRedirect = false
            });

        return services;
    }
}
