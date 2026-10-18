using DotVer.Domain;

namespace DotVer.Application;

public sealed class WorkspaceService : IWorkspaceService
{
    private const int MaxGlobalJsonFiles = 1000;
    private const int MaxDirectoriesVisited = 20000;
    private const int MaxDistinctInstalls = 20;

    private static readonly string[] SkippedDirectories =
        [".git", ".vs", "bin", "obj", "node_modules", "artifacts", "TestResults", ".dotver"];

    private readonly IGlobalJsonService _globalJsonService;
    private readonly IInventoryService _inventoryService;
    private readonly IInstallService _installService;
    private readonly IEnvironmentContext? _environment;

    public WorkspaceService(
        IGlobalJsonService globalJsonService,
        IInventoryService inventoryService,
        IInstallService installService,
        IEnvironmentContext? environment = null)
    {
        _globalJsonService = globalJsonService;
        _inventoryService = inventoryService;
        _installService = installService;
        _environment = environment;
    }

    public async Task<WorkspaceScanResult> ScanAsync(string root, CancellationToken cancellationToken)
    {
        var fullRoot = NormalizeRoot(root);
        var inventory = await _inventoryService.ScanAsync(cancellationToken).ConfigureAwait(false);
        return ScanCore(fullRoot, inventory, cancellationToken);
    }

    public async Task<WorkspaceEnsureResult> EnsureRecursiveAsync(string root, CancellationToken cancellationToken)
    {
        var fullRoot = NormalizeRoot(root);
        var before = await ScanAsync(fullRoot, cancellationToken).ConfigureAwait(false);

        var missingVersions = before.Entries
            .Where(static entry => entry.RequestedVersion is not null && !entry.Satisfied)
            .Select(static entry => entry.RequestedVersion!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var installed = new List<string>();
        var errors = new List<string>();
        errors.AddRange(before.Entries
            .Where(static entry => entry.Invalid)
            .Select(static entry => $"{Sanitize(entry.FilePath)}: invalid global.json (unreadable, linked, oversized or malformed)."));
        if (before.Truncated)
        {
            errors.Add("Scan was truncated; not every pin in the tree was ensured. Narrow the root directory.");
        }
        if (missingVersions.Count > MaxDistinctInstalls)
        {
            errors.Add($"Refusing to install {missingVersions.Count} distinct SDKs in one run (limit {MaxDistinctInstalls}); narrow the scan root.");
            return new WorkspaceEnsureResult(before, installed, errors);
        }

        foreach (var version in missingVersions)
        {
            try
            {
                var outcome = await _installService.InstallAsync(version, options: null, cancellationToken).ConfigureAwait(false);
                installed.Add(outcome.Installation.Version.Raw);
            }
            catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                errors.Add($"{version}: {Sanitize(exception.Message)}");
            }
        }

        var after = await ScanAsync(fullRoot, cancellationToken).ConfigureAwait(false);
        if (after.Truncated)
        {
            errors.Add("Final scan was truncated; not every pin in the tree could be verified. Narrow the root directory.");
        }

        return new WorkspaceEnsureResult(after, installed, errors);
    }

    private WorkspaceScanResult ScanCore(string fullRoot, IReadOnlyList<SdkInstallation> inventory, CancellationToken cancellationToken)
    {
        var files = FindGlobalJsonFiles(fullRoot, cancellationToken, out var truncated);
        var entries = new List<WorkspacePinEntry>();
        foreach (var file in files)
        {
            var read = _globalJsonService.Read(file);
            if (read.Status == GlobalJsonReadStatus.Invalid)
            {
                entries.Add(new WorkspacePinEntry(file, null, null, null, Satisfied: false, Invalid: true));
                continue;
            }

            var request = read.Request;
            if (request is null)
            {
                entries.Add(new WorkspacePinEntry(file, null, null, null, Satisfied: true));
                continue;
            }

            var matched = GlobalJsonMatcher.Match(request, inventory, _environment?.RuntimeArchitecture);
            entries.Add(new WorkspacePinEntry(
                file,
                request.Version.Raw,
                request.RollForward == SdkRollForward.Default ? null : GlobalJsonService.FormatRollForward(request.RollForward),
                matched?.Version.Raw,
                matched is not null));
        }

        entries.Sort(static (a, b) => string.Compare(a.FilePath, b.FilePath, StringComparison.OrdinalIgnoreCase));
        return new WorkspaceScanResult(fullRoot, entries, truncated);
    }

    private static List<string> FindGlobalJsonFiles(string root, CancellationToken cancellationToken, out bool truncated)
    {
        var results = new List<string>();
        truncated = false;
        var pending = new Stack<string>();
        pending.Push(root);
        var discovered = 1;

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var directory = pending.Pop();

            var candidate = Path.Combine(directory, "global.json");
            if (File.Exists(candidate))
            {
                results.Add(candidate);
                if (results.Count >= MaxGlobalJsonFiles)
                {
                    truncated = true;
                    return results;
                }
            }

            try
            {
                foreach (var child in Directory.EnumerateDirectories(directory))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Count every enumerated child before classifying it, so
                    // fan-out of skipped/linked directories is bounded too.
                    if (++discovered > MaxDirectoriesVisited)
                    {
                        truncated = true;
                        return results;
                    }

                    var name = Path.GetFileName(child);
                    if (SkippedDirectories.Contains(name, StringComparer.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    // Never descend through links/junctions: they can escape the
                    // requested root or create cycles.
                    var info = new DirectoryInfo(child);
                    if (info.LinkTarget is not null || (info.Attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        continue;
                    }

                    var relative = Path.GetRelativePath(root, child);
                    var depth = relative.Count(static c => c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar);
                    if (depth >= 32)
                    {
                        truncated = true;
                        continue;
                    }

                    pending.Push(child);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Unreadable directories make coverage incomplete: surface it
                // as truncated instead of silently reporting success.
                truncated = true;
            }
        }

        return results;
    }

    private static string NormalizeRoot(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        var fullRoot = Path.GetFullPath(root);
        return Directory.Exists(fullRoot)
            ? fullRoot
            : throw new InvalidOperationException($"Directory '{fullRoot}' does not exist.");
    }

    private static string Sanitize(string value) =>
        new(value.Where(static c => !char.IsControl(c)).ToArray());
}

