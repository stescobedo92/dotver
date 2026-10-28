using DotVer.Domain;

namespace DotVer.Application;

public sealed class InterceptorService : IInterceptorService
{
    private readonly ISelectionService _selectionService;
    private readonly IInventoryService _inventoryService;
    private readonly IProcessRunner _processRunner;
    private readonly IEnvironmentContext _environment;
    private readonly DotVerPaths _paths;

    public InterceptorService(
        ISelectionService selectionService,
        IInventoryService inventoryService,
        IProcessRunner processRunner,
        IEnvironmentContext environment,
        DotVerPaths paths)
    {
        _selectionService = selectionService;
        _inventoryService = inventoryService;
        _processRunner = processRunner;
        _environment = environment;
        _paths = paths;
    }

    public async Task<ProcessResult> ExecuteAsync(IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        var selection = await _selectionService.ResolveAsync(cancellationToken).ConfigureAwait(false);
        var installations = await _inventoryService.ScanAsync(cancellationToken).ConfigureAwait(false);

        if (args.SequenceEqual(["--version"]))
        {
            var value = selection.Installation?.Version.Raw
                ?? installations.OrderByDescending(static sdk => sdk.Version).FirstOrDefault()?.Version.Raw
                ?? "unknown";

            return new ProcessResult(0, value + Environment.NewLine, string.Empty);
        }

        if (args.SequenceEqual(["--list-sdks"]))
        {
            var output = string.Join(
                Environment.NewLine,
                installations
                    .OrderByDescending(static sdk => sdk.Version)
                    .Select(static sdk => $"{sdk.Version.Raw} [{sdk.SdkRoot}]"));

            return new ProcessResult(0, output + Environment.NewLine, string.Empty);
        }

        // Every host candidate is an absolute path: a bare "dotnet" would let
        // process creation resolve through the current directory (an untrusted
        // repository) on Windows and Unix alike.
        var dotnetPath = selection.Installation?.DotnetPath
            ?? _inventoryService.GetSystemDotnetPath()
            ?? FindHostOnPath();

        if (dotnetPath is null)
        {
            return new ProcessResult(
                1,
                string.Empty,
                "dotver: No .NET SDK is selected and no dotnet host was found in the known install roots or on PATH " +
                "(the current directory is intentionally excluded). Run 'dotver install <version>' or 'dotver use <version>'." +
                Environment.NewLine);
        }

        if (args.SequenceEqual(["--info"]))
        {
            var info = await _processRunner.RunAsync(new ProcessSpec(dotnetPath, args), cancellationToken).ConfigureAwait(false);
            var extra = Environment.NewLine +
                        "DotVer:" + Environment.NewLine +
                        $"  Intercepted: yes{Environment.NewLine}" +
                        $"  Reason: {selection.Reason}{Environment.NewLine}" +
                        $"  Selected SDK: {selection.Installation?.Version.Raw ?? "system-default"}{Environment.NewLine}" +
                        $"  Selected Root: {selection.Installation?.HostRoot ?? "n/a"}{Environment.NewLine}";

            return info with { StandardOutput = info.StandardOutput + extra };
        }

        return await _processRunner.RunAsync(new ProcessSpec(dotnetPath, args), cancellationToken).ConfigureAwait(false);
    }

    private string? FindHostOnPath()
    {
        var fileName = _environment.IsWindows ? "dotnet.exe" : "dotnet";

        // The shims directory holds this very shim: resolving there would recurse.
        return ExecutableLocator.FindOnPath(_environment, fileName, [_paths.Shims]);
    }
}
