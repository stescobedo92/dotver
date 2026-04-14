using DotVer.Domain;

namespace DotVer.Application;

public sealed class ShimService : IShimService
{
    private readonly DotVerPaths _paths;
    private readonly IEnvironmentContext _environment;

    public ShimService(DotVerPaths paths, IEnvironmentContext environment)
    {
        _paths = paths;
        _environment = environment;
    }

    public Task<ShimInstallResult> InstallAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Directory.CreateDirectory(_paths.Shims);
        Directory.CreateDirectory(_paths.Env);

        var files = new List<string>();
        var launcher = BuildLauncher();

        if (_environment.IsWindows)
        {
            files.Add(WriteFile("dotnet.cmd", $"@echo off{Environment.NewLine}{launcher.WindowsInterceptCommand}%*{Environment.NewLine}"));
            files.Add(WriteFile("dotver.ps1", $"{launcher.PowerShellInvoke} @args{Environment.NewLine}"));

            var psEnv = $"$env:PATH = \"{_paths.Shims};$env:PATH\"{Environment.NewLine}" +
                        $"function global:dotnet {{ {launcher.PowerShellInterceptInvoke} @args }}{Environment.NewLine}";

            files.Add(WriteEnvFile("dotver.ps1", psEnv));
            files.Add(WriteEnvFile("dotver.cmd", $"@echo off{Environment.NewLine}set PATH={_paths.Shims};%PATH%{Environment.NewLine}"));
        }
        else
        {
            files.Add(WriteExecutableFile("dotnet", $"#!/usr/bin/env bash{Environment.NewLine}{launcher.ShellInterceptCommand}\"$@\"{Environment.NewLine}"));
            files.Add(WriteEnvFile("dotver.sh", $"export PATH=\"{_paths.Shims}:$PATH\"{Environment.NewLine}"));
            files.Add(WriteEnvFile("dotver.fish", $"set -gx PATH \"{_paths.Shims}\" $PATH{Environment.NewLine}"));
        }

        return Task.FromResult(new ShimInstallResult(_paths.Shims, files));
    }

    public string RenderEnvSnippet(ShellKind shellKind) =>
        shellKind switch
        {
            ShellKind.PowerShell => $". \"{Path.Combine(_paths.Env, "dotver.ps1")}\"",
            ShellKind.Cmd => $"call \"{Path.Combine(_paths.Env, "dotver.cmd")}\"",
            ShellKind.Fish => $"source \"{Path.Combine(_paths.Env, "dotver.fish")}\"",
            _ => $"source \"{Path.Combine(_paths.Env, "dotver.sh")}\""
        };

    private string WriteFile(string name, string content)
    {
        var path = Path.Combine(_paths.Shims, name);
        WriteTextIfChanged(path, content);
        return path;
    }

    private string WriteEnvFile(string name, string content)
    {
        var path = Path.Combine(_paths.Env, name);
        WriteTextIfChanged(path, content);
        return path;
    }

    private string WriteExecutableFile(string name, string content)
    {
        var path = WriteFile(name, content);
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                                       UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                                       UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }

        return path;
    }

    private static void WriteTextIfChanged(string path, string content)
    {
        if (File.Exists(path))
        {
            var existing = File.ReadAllText(path);
            if (string.Equals(existing, content, StringComparison.Ordinal))
            {
                return;
            }
        }

        File.WriteAllText(path, content);
    }

    private ShimLauncher BuildLauncher()
    {
        var processPath = _environment.ProcessPath;
        var entryAssemblyPath = _environment.EntryAssemblyPath;

        var runsAsManagedDll = Path.GetExtension(entryAssemblyPath).Equals(".dll", StringComparison.OrdinalIgnoreCase) &&
                               Path.GetFileName(processPath).StartsWith("dotnet", StringComparison.OrdinalIgnoreCase);

        if (runsAsManagedDll)
        {
            return new ShimLauncher(
                $"\"{processPath}\" \"{entryAssemblyPath}\" ",
                $"& \"{processPath}\" \"{entryAssemblyPath}\"",
                $"\"{processPath}\" \"{entryAssemblyPath}\" intercept -- ",
                $"& \"{processPath}\" \"{entryAssemblyPath}\" intercept --");
        }

        return new ShimLauncher(
            $"\"{processPath}\" ",
            $"& \"{processPath}\"",
            $"\"{processPath}\" intercept -- ",
            $"& \"{processPath}\" intercept --");
    }

    private sealed record ShimLauncher(string ShellInvokePrefix, string PowerShellInvoke, string ShellInterceptCommand, string PowerShellInterceptInvoke)
    {
        public string WindowsInterceptCommand => ShellInterceptCommand;
    }
}
