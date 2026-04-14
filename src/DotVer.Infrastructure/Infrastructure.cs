using System.Diagnostics;
using System.Reflection;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using DotVer.Application;
using DotVer.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace DotVer.Infrastructure;

public sealed class SystemEnvironmentContext : IEnvironmentContext
{
    public string HomeDirectory =>
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public string DocumentsDirectory =>
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    public string CurrentDirectory => Directory.GetCurrentDirectory();

    public string ProcessPath => Environment.ProcessPath ?? "dotver";

    public string EntryAssemblyPath => Assembly.GetEntryAssembly()?.Location ?? ProcessPath;

    public bool IsWindows => OperatingSystem.IsWindows();

    public bool IsLinux => OperatingSystem.IsLinux();

    public bool IsMacOS => OperatingSystem.IsMacOS();

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

    public string? GetEnvironmentVariable(string name) => Environment.GetEnvironmentVariable(name);

    public string? GetUserEnvironmentVariable(string name) =>
        Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User);

    public void SetUserEnvironmentVariable(string name, string value) =>
        Environment.SetEnvironmentVariable(name, value, EnvironmentVariableTarget.User);
}

public sealed class JsonConfigStore : IConfigStore
{
    private readonly DotVerPaths _paths;

    public JsonConfigStore(DotVerPaths paths)
    {
        _paths = paths;
    }

    public async Task<DotVerConfig> LoadAsync(CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_paths.ConfigFile)!;
        Directory.CreateDirectory(directory);

        if (!File.Exists(_paths.ConfigFile))
        {
            return DotVerConfig.Empty;
        }

        await using var stream = File.OpenRead(_paths.ConfigFile);
        var config = await JsonSerializer.DeserializeAsync(stream, DotVerJsonContext.Default.DotVerConfig, cancellationToken).ConfigureAwait(false);
        return config ?? DotVerConfig.Empty;
    }

    public async Task SaveAsync(DotVerConfig config, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_paths.ConfigFile)!;
        Directory.CreateDirectory(directory);

        await using var stream = File.Create(_paths.ConfigFile);
        await JsonSerializer.SerializeAsync(stream, config, DotVerJsonContext.Default.DotVerConfig, cancellationToken).ConfigureAwait(false);
    }
}

public sealed class ProcessRunner : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = spec.FileName,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };

        foreach (var argument in spec.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (!string.IsNullOrWhiteSpace(spec.WorkingDirectory))
        {
            startInfo.WorkingDirectory = spec.WorkingDirectory;
        }

        if (spec.EnvironmentVariables is not null)
        {
            foreach (var entry in spec.EnvironmentVariables)
            {
                if (entry.Value is null)
                {
                    startInfo.Environment.Remove(entry.Key);
                }
                else
                {
                    startInfo.Environment[entry.Key] = entry.Value;
                }
            }
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        return new ProcessResult(process.ExitCode, stdout, stderr);
    }
}

public sealed class HttpResourceClient : IHttpResourceClient
{
    private readonly HttpClient _httpClient;

    public HttpResourceClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("dotver", "1.0.0"));
    }

    public async Task<string> GetStringAsync(string url, CancellationToken cancellationToken) =>
        await _httpClient.GetStringAsync(url, cancellationToken).ConfigureAwait(false);

    public async Task DownloadFileAsync(string url, string destinationPath, CancellationToken cancellationToken)
    {
        var bytes = await _httpClient.GetByteArrayAsync(url, cancellationToken).ConfigureAwait(false);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        await File.WriteAllBytesAsync(destinationPath, bytes, cancellationToken).ConfigureAwait(false);
    }
}

[JsonSerializable(typeof(DotVerConfig))]
internal sealed partial class DotVerJsonContext : JsonSerializerContext;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDotVer(this IServiceCollection services)
    {
        services.AddSingleton<IEnvironmentContext, SystemEnvironmentContext>();
        services.AddSingleton(static provider => DotVerPaths.From(provider.GetRequiredService<IEnvironmentContext>()));
        services.AddSingleton<IConfigStore, JsonConfigStore>();
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddHttpClient<IHttpResourceClient, HttpResourceClient>();

        services.AddSingleton<GlobalJsonService>();
        services.AddSingleton<VersionResolver>();
        services.AddSingleton<InventoryService>();
        services.AddSingleton<SelectionService>();
        services.AddSingleton<InterceptorService>();
        services.AddSingleton<ShimService>();
        services.AddSingleton<EnvironmentBootstrapService>();
        services.AddSingleton<InstallService>();
        services.AddSingleton<StateService>();

        return services;
    }
}
