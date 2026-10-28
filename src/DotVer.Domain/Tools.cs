namespace DotVer.Domain;

/// <summary>A global dotnet tool that dotver keeps installed for every SDK it installs.</summary>
public sealed record ToolEntry(string Id, string? Version);

public sealed record ToolsManifest(IReadOnlyList<ToolEntry> Tools)
{
    public static ToolsManifest Empty => new([]);
}

public sealed record ToolSyncEntry(string Id, string? Version, bool Succeeded, int ExitCode, string? Error);

public sealed record ToolSyncResult(SdkInstallation Sdk, IReadOnlyList<ToolSyncEntry> Tools)
{
    public int Failed => Tools.Count(static tool => !tool.Succeeded);
}

/// <summary>Outcome of a user hook under ~/.dotver/hooks. Ran=false means no hook exists for the event.</summary>
public sealed record HookRunResult(string Event, string? Script, bool Ran, int ExitCode, string? Output);
