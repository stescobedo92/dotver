using DotVer.Domain;

namespace DotVer.Application;

public sealed class InterceptorService : IInterceptorService
{
    private readonly ISelectionService _selectionService;
    private readonly IInventoryService _inventoryService;
    private readonly IProcessRunner _processRunner;

    public InterceptorService(ISelectionService selectionService, IInventoryService inventoryService, IProcessRunner processRunner)
    {
        _selectionService = selectionService;
        _inventoryService = inventoryService;
        _processRunner = processRunner;
    }

    public async Task<ProcessResult> ExecuteAsync(IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        var selection = await _selectionService.ResolveAsync(cancellationToken).ConfigureAwait(false);
        var installations = await _inventoryService.ScanAsync(cancellationToken).ConfigureAwait(false);
        var dotnetPath = selection.Installation?.DotnetPath ?? _inventoryService.GetSystemDotnetPath() ?? "dotnet";

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
}
