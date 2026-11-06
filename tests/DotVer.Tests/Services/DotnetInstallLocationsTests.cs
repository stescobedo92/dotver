using DotVer.Application;
using DotVer.Infrastructure;

namespace DotVer.Tests;

public sealed class DotnetInstallLocationsTests
{
    [Fact]
    public void WindowsRegistryKey_IsPerArchitectureUnderSetupInstalledVersions()
    {
        Assert.Equal(@"SOFTWARE\dotnet\Setup\InstalledVersions\arm64", DotnetInstallLocations.WindowsRegistryKey("ARM64"));
        Assert.Equal("InstallLocation", DotnetInstallLocations.WindowsRegistryValueName);
    }

    [Fact]
    public void WindowsDefaultInstallRoot_FollowsProgramFilesPerArchitecture()
    {
        Assert.Equal(Path.Combine(@"C:\PF", "dotnet"), DotnetInstallLocations.WindowsDefaultInstallRoot(@"C:\PF", @"C:\PF86", "x64"));
        Assert.Equal(Path.Combine(@"C:\PF86", "dotnet"), DotnetInstallLocations.WindowsDefaultInstallRoot(@"C:\PF", @"C:\PF86", "x86"));
        Assert.Equal(Path.Combine(@"C:\PF", "dotnet", "x64"), DotnetInstallLocations.WindowsDefaultInstallRoot(@"C:\PF", @"C:\PF86", "x64", hostArchitecture: "arm64"));
        Assert.Null(DotnetInstallLocations.WindowsDefaultInstallRoot(null, null, "x64"));
    }

    [Fact]
    public void UnixInstallLocationFiles_ListTheArchitectureSpecificFileFirst()
    {
        Assert.Equal(
            ["/etc/dotnet/install_location_arm64", "/etc/dotnet/install_location"],
            DotnetInstallLocations.UnixInstallLocationFiles("arm64"));
    }

    [Theory]
    [InlineData("/usr/lib64/dotnet\n", "/usr/lib64/dotnet")]
    [InlineData("/opt/dotnet/", "/opt/dotnet")]
    [InlineData("  \n\n", null)]
    [InlineData("relative/dotnet", null)]
    [InlineData(null, null)]
    public void ParseInstallLocationFile_TakesTheFirstAbsolutePathLine(string? content, string? expected)
    {
        Assert.Equal(expected, DotnetInstallLocations.ParseInstallLocationFile(content));
    }

    [Fact]
    public void Resolve_OnWindows_PrefersTheRegisteredLocationThenTheDefault()
    {
        using var sandbox = new TestSandbox(TestPlatform.Windows);
        var environment = sandbox.CreateEnvironment();
        var programFiles = environment.GetEnvironmentVariable("ProgramFiles")!;
        var queriedKeys = new List<string>();

        var locations = DotnetInstallLocations.Resolve(
            environment,
            key =>
            {
                queriedKeys.Add(key);
                return @"C:\custom\dotnet\";
            },
            static _ => throw new InvalidOperationException("Windows never reads /etc/dotnet."));

        Assert.Equal([@"SOFTWARE\dotnet\Setup\InstalledVersions\x64"], queriedKeys);
        Assert.Equal(2, locations.Count);
        Assert.Equal(@"C:\custom\dotnet", locations[0].Path);
        Assert.Contains("HKLM", locations[0].Source, StringComparison.Ordinal);
        Assert.Equal(Path.Combine(programFiles, "dotnet"), locations[1].Path);
    }

    [Fact]
    public void Resolve_OnWindows_DeduplicatesCaseInsensitively()
    {
        using var sandbox = new TestSandbox(TestPlatform.Windows);
        var environment = sandbox.CreateEnvironment();
        var programFiles = environment.GetEnvironmentVariable("ProgramFiles")!;

        var locations = DotnetInstallLocations.Resolve(
            environment,
            _ => Path.Combine(programFiles, "DOTNET") + "\\",
            static _ => null);

        var location = Assert.Single(locations);
        Assert.Contains("HKLM", location.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_OnLinux_ReadsEtcDotnetInstallLocationThenTheDefault()
    {
        using var sandbox = new TestSandbox(TestPlatform.Linux);
        var environment = sandbox.CreateEnvironment();

        var locations = DotnetInstallLocations.Resolve(
            environment,
            static _ => throw new InvalidOperationException("Linux never reads the registry."),
            path => path == "/etc/dotnet/install_location_x64" ? "/opt/dotnet\n" : null);

        Assert.Equal(2, locations.Count);
        Assert.Equal("/opt/dotnet", locations[0].Path);
        Assert.Equal("/etc/dotnet/install_location_x64", locations[0].Source);
        Assert.Equal("/usr/share/dotnet", locations[1].Path);
    }

    [Fact]
    public void Resolve_OnLinux_FallsBackToTheGenericFileAndSkipsTheDefaultWhenEqual()
    {
        using var sandbox = new TestSandbox(TestPlatform.Linux);
        var environment = sandbox.CreateEnvironment();

        var locations = DotnetInstallLocations.Resolve(
            environment,
            static _ => null,
            path => path == "/etc/dotnet/install_location" ? "/usr/share/dotnet/" : null);

        var location = Assert.Single(locations);
        Assert.Equal("/usr/share/dotnet", location.Path);
        Assert.Equal("/etc/dotnet/install_location", location.Source);
    }

    [Fact]
    public void Resolve_OnMacOS_DefaultsToUsrLocalShare()
    {
        using var sandbox = new TestSandbox(TestPlatform.MacOS);
        var environment = sandbox.CreateEnvironment();

        var locations = DotnetInstallLocations.Resolve(environment, static _ => null, static _ => null);

        var location = Assert.Single(locations);
        Assert.Equal("/usr/local/share/dotnet", location.Path);
    }

    [Fact]
    public void SystemEnvironmentContext_KnownRoots_StartWithTheHostGlobalLocation()
    {
        var context = new SystemEnvironmentContext();

        var roots = context.GetKnownDotnetRoots();

        if (OperatingSystem.IsWindows())
        {
            var programFiles = Environment.GetEnvironmentVariable("ProgramFiles")!;
            Assert.Contains(Path.Combine(programFiles, "dotnet"), roots);
        }
        else
        {
            Assert.Contains(OperatingSystem.IsMacOS() ? "/usr/local/share/dotnet" : "/usr/share/dotnet", roots);
        }

        Assert.Equal(roots.Count, roots.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}
