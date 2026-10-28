using DotVer.Domain;

namespace DotVer.Application;

public interface IToolsManifestStore
{
    string FilePath { get; }

    Task<ToolsManifest> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(ToolsManifest manifest, CancellationToken cancellationToken);
}

public interface IToolSyncService
{
    Task<ToolsManifest> ListAsync(CancellationToken cancellationToken);

    Task<ToolsManifest> AddAsync(string id, string? version, CancellationToken cancellationToken);

    Task<bool> RemoveAsync(string id, CancellationToken cancellationToken);

    /// <summary>Installs or updates every manifest tool with the given SDK host. Failures are reported, never thrown.</summary>
    Task<ToolSyncResult> SyncAsync(SdkInstallation sdk, CancellationToken cancellationToken);
}

public interface IHookRunner
{
    /// <summary>Runs ~/.dotver/hooks/&lt;event&gt; (or .ps1/.cmd/.bat/.exe on Windows) with the SDK in its environment, if it exists.</summary>
    Task<HookRunResult> RunAsync(string hookEvent, SdkInstallation sdk, CancellationToken cancellationToken);
}
