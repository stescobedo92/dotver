using DotVer.Domain;

namespace DotVer.Application;

public sealed class InfoService : IInfoService
{
    private static readonly string[] FrameworkDirectories =
        ["Microsoft.NETCore.App", "Microsoft.AspNetCore.App", "Microsoft.WindowsDesktop.App"];

    private readonly IExecService _execService;
    private readonly ISelectionService _selectionService;

    public InfoService(IExecService execService, ISelectionService selectionService)
    {
        _execService = execService;
        _selectionService = selectionService;
    }

    public async Task<SdkInfo> GetInfoAsync(string? token, CancellationToken cancellationToken)
    {
        SdkInstallation sdk;
        if (!string.IsNullOrWhiteSpace(token))
        {
            sdk = await _execService.ResolveSdkAsync(token, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var selection = await _selectionService.ResolveAsync(cancellationToken).ConfigureAwait(false);
            sdk = selection.Installation
                ?? throw new InvalidOperationException("No SDK is currently selected. Pass a token, e.g. 'dotver info 8.0'.");
        }

        var runtimes = FrameworkDirectories
            .Select(framework => new RuntimeInfo(framework, ListVersions(Path.Combine(sdk.HostRoot, "shared", framework))))
            .Where(static runtime => runtime.Versions.Count > 0)
            .ToList();

        return new SdkInfo(sdk, runtimes, ListWorkloads(sdk.HostRoot));
    }

    private static IReadOnlyList<string> ListVersions(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        // Full strict version validation: the whole directory name must parse,
        // not just a prefix (names flow into terminal output).
        return Directory.EnumerateDirectories(directory)
            .Select(Path.GetFileName)
            .Where(static name => name is not null && SdkVersion.TryParse(name, out _))
            .Select(static name => name!)
            .OrderBy(static name => Version.Parse(name.Split('-', '+')[0]))
            .ToList();
    }

    private static IReadOnlyList<string> ListWorkloads(string hostRoot)
    {
        var manifests = Path.Combine(hostRoot, "sdk-manifests");
        if (!Directory.Exists(manifests))
        {
            return [];
        }

        var workloads = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var band in Directory.EnumerateDirectories(manifests))
        {
            foreach (var workload in Directory.EnumerateDirectories(band))
            {
                var name = Path.GetFileName(workload);
                // Workload ids are restricted identifiers; anything else (control
                // characters, markup, spaces) is dropped, never displayed.
                if (!string.IsNullOrWhiteSpace(name) &&
                    name.All(static c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_'))
                {
                    workloads.Add(name);
                }
            }
        }

        return [.. workloads];
    }
}
