namespace DotVer.Application;

/// <summary>Architecture names accepted by the official dotnet-install script.</summary>
public static class Architectures
{
    private static readonly HashSet<string> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        "x64", "x86", "arm64", "arm", "s390x", "ppc64le", "loongarch64", "riscv64",
        "musl-x64", "musl-arm64", "musl-arm", "musl-s390x"
    };

    public static bool IsKnown(string? value) =>
        value is not null && Known.Contains(value.Trim());

    public static string Normalize(string? value, string hostArchitecture)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return hostArchitecture;
        }

        var normalized = value.Trim().ToLowerInvariant();
        return Known.Contains(normalized)
            ? normalized
            : throw new InvalidOperationException(
                $"Unknown architecture '{TerminalText.Sanitize(value.Trim())}'. Supported: {string.Join(", ", Known.OrderBy(static a => a, StringComparer.Ordinal))}.");
    }

    public static bool MatchesHost(string? installationArchitecture, string hostArchitecture) =>
        installationArchitecture is null ||
        string.Equals(installationArchitecture, hostArchitecture, StringComparison.OrdinalIgnoreCase);
}
