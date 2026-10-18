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

        return services;
    }
}
