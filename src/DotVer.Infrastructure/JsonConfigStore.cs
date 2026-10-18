using System.Text.Json;
using System.Text.Json.Serialization;
using DotVer.Application;
using DotVer.Domain;

namespace DotVer.Infrastructure;

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

        // Atomic replace: never truncate the live config in place.
        var temp = Path.Combine(directory, $".config.json.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, config, DotVerJsonContext.Default.DotVerConfig, cancellationToken).ConfigureAwait(false);
            }

            File.Move(temp, _paths.ConfigFile, overwrite: true);
        }
        catch
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }

            throw;
        }
    }
}

[JsonSerializable(typeof(DotVerConfig))]
internal sealed partial class DotVerJsonContext : JsonSerializerContext;
