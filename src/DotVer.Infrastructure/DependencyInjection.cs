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
        // Scripts and the releases index: HttpResourceClient validates every
        // redirect hop against its host allow-list, so the handler must not
        // follow redirects on its own.
        // HTTPS_PROXY / HTTP_PROXY / NO_PROXY (and the Windows system proxy) are
        // honored through HttpClient.DefaultProxy; stated explicitly on purpose.
        services.AddHttpClient<IHttpResourceClient, HttpResourceClient>()
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AllowAutoRedirect = false,
                UseProxy = true,
                Proxy = HttpClient.DefaultProxy
            });

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
        services.AddSingleton<IMatrixService, MatrixService>();
        services.AddSingleton<IInfoService, InfoService>();
        services.AddSingleton<IIdeSettingsService, IdeSettingsService>();
        services.AddSingleton<IToolsManifestStore, ToolsManifestStore>();
        services.AddSingleton<IToolSyncService, ToolSyncService>();
        services.AddSingleton<IHookRunner, HookRunner>();
        services.AddSingleton<IDownloadSources, ConfigDownloadSources>();
        services.AddSingleton<IMirrorService, MirrorService>();
        services.AddSingleton<ISelfUpdateService, SelfUpdateService>();
        services.AddSingleton<ISelfUninstallService, SelfUninstallService>();

        // dotver's own releases: GitHub API + asset CDN only, redirects validated hop by hop.
        services.AddHttpClient<IReleaseFeed, GitHubReleaseFeed>()
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AllowAutoRedirect = false,
                UseProxy = true,
                Proxy = HttpClient.DefaultProxy
            });

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
