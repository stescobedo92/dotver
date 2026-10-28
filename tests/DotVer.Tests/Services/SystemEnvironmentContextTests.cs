using DotVer.Infrastructure;

namespace DotVer.Tests;

public sealed class SystemEnvironmentContextTests
{
    [Fact]
    public void EntryAssemblyPath_IsNeverEmpty()
    {
        var context = new SystemEnvironmentContext();

        Assert.False(string.IsNullOrWhiteSpace(context.EntryAssemblyPath));
        Assert.Equal(Path.GetFullPath(Environment.GetCommandLineArgs()[0]), context.EntryAssemblyPath);
    }

    [Fact]
    public void GetKnownDotnetRoots_IncludesDotnetInstallDefaultLocation()
    {
        var context = new SystemEnvironmentContext();

        // dotnet-install.ps1/.sh default to these when -InstallDir is omitted;
        // missing them makes the interceptor fall back to an unqualified host.
        var expected = OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "dotnet")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet");

        Assert.Contains(expected, context.GetKnownDotnetRoots());
    }

    [Fact]
    public void UserEnvironmentVariable_RoundTripPreservesUnexpandedExpandStringValue()
    {
        if (!OperatingSystem.IsWindows())
        {
            return; // the user-scope registry only exists on Windows
        }

        var name = "DOTVER_TEST_" + Guid.NewGuid().ToString("N");
        const string raw = @"%USERPROFILE%\bin";
        using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey("Environment", writable: true)!;
        try
        {
            key.SetValue(name, raw, Microsoft.Win32.RegistryValueKind.ExpandString);
            var context = new SystemEnvironmentContext();

            // Read the stored text, not its expansion, so a rewrite cannot
            // bake the current %USERPROFILE% into the value.
            Assert.Equal(raw, context.GetUserEnvironmentVariable(name));

            context.SetUserEnvironmentVariable(name, @"C:\shims;" + raw);

            Assert.Equal(Microsoft.Win32.RegistryValueKind.ExpandString, key.GetValueKind(name));
            Assert.Equal(@"C:\shims;" + raw, key.GetValue(name, null, Microsoft.Win32.RegistryValueOptions.DoNotExpandEnvironmentNames));
        }
        finally
        {
            key.DeleteValue(name, throwOnMissingValue: false);
        }
    }
}
