using System.Reflection;
using System.Runtime.InteropServices;
using DotVer.Application;
using Microsoft.Win32;

namespace DotVer.Infrastructure;

public sealed partial class SystemEnvironmentContext : IEnvironmentContext
{
    private const string UserEnvironmentKey = "Environment";

    public string HomeDirectory =>
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public string DocumentsDirectory =>
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    public string CurrentDirectory => Directory.GetCurrentDirectory();

    public string ProcessPath => Environment.ProcessPath ?? "dotver";

    // Assembly.Location is empty (and IL3000) in single-file apps. The first
    // command-line arg is the managed dll when launched as `dotnet dotver.dll`
    // and the executable path otherwise, which is all ShimService needs.
    public string EntryAssemblyPath
    {
        get
        {
            var args = Environment.GetCommandLineArgs();
            if (args.Length == 0 || string.IsNullOrWhiteSpace(args[0]))
            {
                return ProcessPath;
            }

            try
            {
                var fullPath = Path.GetFullPath(args[0]);
                return File.Exists(fullPath) ? fullPath : ProcessPath;
            }
            catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
            {
                return ProcessPath;
            }
        }
    }

    public bool IsWindows => OperatingSystem.IsWindows();

    public bool IsLinux => OperatingSystem.IsLinux();

    public bool IsMacOS => OperatingSystem.IsMacOS();

    public string RuntimeArchitecture => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.X64 => "x64",
        Architecture.X86 => "x86",
        Architecture.Arm64 => "arm64",
        Architecture.Arm => "arm",
        Architecture.S390x => "s390x",
        Architecture.Ppc64le => "ppc64le",
        Architecture.LoongArch64 => "loongarch64",
        Architecture.RiscV64 => "riscv64",
        _ => RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()
    };

    // <Version> flows into AssemblyInformationalVersion at publish time; the
    // "+<commit>" build metadata is not part of the comparable version.
    public string ProductVersion
    {
        get
        {
            var informational = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (string.IsNullOrWhiteSpace(informational))
            {
                return "0.0.0";
            }

            var plus = informational.IndexOf('+', StringComparison.Ordinal);
            return plus >= 0 ? informational[..plus] : informational;
        }
    }

    public IReadOnlyList<string> GetPathEntries()
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return [];
        }

        return path
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(IsWindows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
            .ToList();
    }

    public IReadOnlyList<string> GetKnownDotnetRoots()
    {
        if (!IsWindows)
        {
            var unixRoots = new List<string> { "/usr/share/dotnet", "/usr/local/share/dotnet", "/usr/lib/dotnet" };

            // dotnet-install.sh installs here when --install-dir is omitted.
            var home = HomeDirectory;
            if (!string.IsNullOrWhiteSpace(home))
            {
                unixRoots.Add(Path.Combine(home, ".dotnet"));
            }

            return unixRoots;
        }

        var roots = new List<string>();
        var programFiles = Environment.GetEnvironmentVariable("ProgramFiles");
        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            roots.Add(Path.Combine(programFiles, "dotnet"));
        }

        var programFilesX86 = Environment.GetEnvironmentVariable("ProgramFiles(x86)");
        if (!string.IsNullOrWhiteSpace(programFilesX86))
        {
            roots.Add(Path.Combine(programFilesX86, "dotnet"));
        }

        // dotnet-install.ps1 installs here when -InstallDir is omitted.
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            roots.Add(Path.Combine(localAppData, "Microsoft", "dotnet"));
        }

        return roots;
    }

    public string? GetEnvironmentVariable(string name) => Environment.GetEnvironmentVariable(name);

    // User-scope variables go through the registry directly: the Environment
    // class expands REG_EXPAND_SZ values on read and writes REG_SZ back, which
    // would bake %USERPROFILE% and friends into the user PATH on every rewrite.
    public string? GetUserEnvironmentVariable(string name)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User);
        }

        using var key = Registry.CurrentUser.OpenSubKey(UserEnvironmentKey, writable: false);
        return key?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
    }

    public void SetUserEnvironmentVariable(string name, string value)
    {
        if (!OperatingSystem.IsWindows())
        {
            Environment.SetEnvironmentVariable(name, value, EnvironmentVariableTarget.User);
            return;
        }

        using var key = Registry.CurrentUser.CreateSubKey(UserEnvironmentKey, writable: true)
            ?? throw new InvalidOperationException("Could not open the user Environment registry key.");

        // Keep the existing kind; anything that references a variable must stay expandable.
        var kind = ExistingKind(key, name);
        if (value.Contains('%', StringComparison.Ordinal))
        {
            kind = RegistryValueKind.ExpandString;
        }

        key.SetValue(name, value, kind);
        key.Flush();
        BroadcastEnvironmentChange();
    }

    public void DeleteUserEnvironmentVariable(string name)
    {
        if (!OperatingSystem.IsWindows())
        {
            Environment.SetEnvironmentVariable(name, null, EnvironmentVariableTarget.User);
            return;
        }

        using var key = Registry.CurrentUser.OpenSubKey(UserEnvironmentKey, writable: true);
        if (key is null)
        {
            return;
        }

        key.DeleteValue(name, throwOnMissingValue: false);
        key.Flush();
        BroadcastEnvironmentChange();
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static RegistryValueKind ExistingKind(RegistryKey key, string name)
    {
        try
        {
            return key.GetValueKind(name) == RegistryValueKind.ExpandString
                ? RegistryValueKind.ExpandString
                : RegistryValueKind.String;
        }
        catch (IOException)
        {
            // The value does not exist yet.
            return RegistryValueKind.String;
        }
    }

    // Environment.SetEnvironmentVariable(User) does this implicitly; raw
    // registry writes must notify Explorer so new terminals see the change.
    private static void BroadcastEnvironmentChange()
    {
        const nint hwndBroadcast = 0xFFFF;
        const uint wmSettingChange = 0x001A;
        const uint smtoAbortIfHung = 0x0002;

        try
        {
            _ = SendMessageTimeoutW(hwndBroadcast, wmSettingChange, 0, "Environment", smtoAbortIfHung, 1000, out _);
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            // Best effort: the registry value is already written.
        }
    }

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial nint SendMessageTimeoutW(nint hWnd, uint msg, nuint wParam, string lParam, uint flags, uint timeout, out nuint result);
}
