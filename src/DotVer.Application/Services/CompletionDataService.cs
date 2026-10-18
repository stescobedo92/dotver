using System.Text.RegularExpressions;

namespace DotVer.Application;

public sealed partial class CompletionDataService : ICompletionDataService
{
    private readonly IInventoryService _inventoryService;
    private readonly IConfigStore _configStore;

    public CompletionDataService(IInventoryService inventoryService, IConfigStore configStore)
    {
        _inventoryService = inventoryService;
        _configStore = configStore;
    }

    public async Task<IReadOnlyList<string>> GetCandidatesAsync(string kind, CancellationToken cancellationToken)
    {
        switch (kind.Trim().ToLowerInvariant())
        {
            case "versions":
                return SafeVersions(await LoadVersionRawsAsync(cancellationToken).ConfigureAwait(false));

            case "aliases":
            {
                var config = await _configStore.LoadAsync(cancellationToken).ConfigureAwait(false);
                return SafeAliases(config.Aliases.Keys);
            }

            case "tokens":
            case "sdk-tokens":
            {
                var versions = SafeVersions(await LoadVersionRawsAsync(cancellationToken).ConfigureAwait(false));
                var config = await _configStore.LoadAsync(cancellationToken).ConfigureAwait(false);
                var tokens = new List<string> { "lts", "sts", "latest" };

                // `system` only means "clear the persisted selection" for use/default.
                if (string.Equals(kind.Trim(), "tokens", StringComparison.OrdinalIgnoreCase))
                {
                    tokens.Add("system");
                }

                tokens.AddRange(SafeAliases(config.Aliases.Keys));
                tokens.AddRange(versions);
                return tokens.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            }

            default:
                return [];
        }
    }

    private async Task<List<string>> LoadVersionRawsAsync(CancellationToken cancellationToken)
    {
        var inventory = await _inventoryService.ScanAsync(cancellationToken).ConfigureAwait(false);
        return inventory.Select(static sdk => sdk.Version.Raw).ToList();
    }

    // Completion output is consumed by shells (bash re-expands compgen -W word
    // lists), so every candidate is filtered here at the boundary, regardless of
    // invariants of the producing types. Only shell-inert charsets pass.
    private static List<string> SafeVersions(IEnumerable<string> versions) =>
        versions
            .Where(static version => SafeVersionPattern().IsMatch(version))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static List<string> SafeAliases(IEnumerable<string> aliases) =>
        aliases
            .Where(static alias => SafeTokenPattern().IsMatch(alias))
            .OrderBy(static alias => alias, StringComparer.OrdinalIgnoreCase)
            .ToList();

    [GeneratedRegex(@"\A[0-9][0-9A-Za-z.+-]*\z", RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex SafeVersionPattern();

    [GeneratedRegex(@"\A[A-Za-z0-9][A-Za-z0-9._-]*\z", RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex SafeTokenPattern();
}
