using DotVer.Domain;

namespace DotVer.Application;

public sealed class ExecService : IExecService
{
    private readonly IInventoryService _inventoryService;
    private readonly IConfigStore _configStore;
    private readonly IVersionResolver _versionResolver;
    private readonly IProcessRunner _processRunner;
    private readonly IEnvironmentContext _environment;

    public ExecService(
        IInventoryService inventoryService,
        IConfigStore configStore,
        IVersionResolver versionResolver,
        IProcessRunner processRunner,
        IEnvironmentContext environment)
    {
        _inventoryService = inventoryService;
        _configStore = configStore;
        _versionResolver = versionResolver;
        _processRunner = processRunner;
        _environment = environment;
    }

    public async Task<SdkInstallation> ResolveSdkAsync(string token, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        var inventory = await _inventoryService.ScanAsync(cancellationToken).ConfigureAwait(false);
        var config = await _configStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        return _versionResolver.FindByToken(token, config, inventory)
            ?? throw new InvalidOperationException($"No selectable version found for '{Sanitize(token)}'. Run 'dotver install {Sanitize(token)}' first.");
    }

    public async Task<ProcessResult> ExecuteAsync(string token, IReadOnlyList<string> command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.Count == 0 || string.IsNullOrWhiteSpace(command[0]))
        {
            throw new InvalidOperationException("Usage: dotver exec <version> -- <command> [args...]");
        }

        var sdk = await ResolveSdkAsync(token, cancellationToken).ConfigureAwait(false);
        var fileName = ResolveFileName(command[0], sdk);
        var environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["DOTNET_ROOT"] = sdk.HostRoot,
            ["DOTVER_VERSION"] = sdk.Version.Raw
        };

        var spec = new ProcessSpec(
            fileName,
            [.. command.Skip(1)],
            WorkingDirectory: _environment.CurrentDirectory,
            EnvironmentVariables: environment,
            InheritStandardIo: true);

        return await _processRunner.RunAsync(spec, cancellationToken).ConfigureAwait(false);
    }

    private string ResolveFileName(string requested, SdkInstallation sdk)
    {
        var bare = Path.GetFileName(requested);
        if (string.Equals(bare, "dotnet", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(bare, "dotnet.exe", StringComparison.OrdinalIgnoreCase))
        {
            return sdk.DotnetPath;
        }

        var hasDirectory = requested.Contains(Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
                           requested.Contains(Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
        if (hasDirectory)
        {
            // Explicit path typed by the user: honor it as given.
            return requested;
        }

        // Bare names must resolve from PATH, never from the current directory.
        var found = ExecutableLocator.FindOnPath(_environment, requested);
        if (found is null && _environment.IsWindows && !requested.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            found = ExecutableLocator.FindOnPath(_environment, requested + ".exe");
        }

        return found ?? throw new InvalidOperationException(
            $"Command '{Sanitize(requested)}' was not found on PATH (the current directory is intentionally excluded).");
    }

    private static string Sanitize(string value) =>
        new(value.Where(static c => !char.IsControl(c)).ToArray());
}
