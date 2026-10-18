using System.Diagnostics;
using DotVer.Domain;

namespace DotVer.Application;

public sealed class MatrixService : IMatrixService
{
    private const int MaxTokens = 16;
    private const int MaxParallelism = 4;

    private readonly IExecService _execService;

    public MatrixService(IExecService execService)
    {
        _execService = execService;
    }

    public async Task<MatrixResult> RunAsync(
        IReadOnlyList<string> tokens,
        IReadOnlyList<string> command,
        bool parallel,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(command);
        if (tokens.Count == 0)
        {
            throw new InvalidOperationException("Usage: dotver each <token[,token...]> [--parallel] [--json] -- <command> [args...]");
        }

        if (tokens.Count > MaxTokens)
        {
            throw new InvalidOperationException($"Too many version tokens ({tokens.Count}); the limit per matrix run is {MaxTokens}.");
        }

        if (command.Count == 0)
        {
            throw new InvalidOperationException("Usage: dotver each <token[,token...]> [--parallel] [--json] -- <command> [args...]");
        }

        // Resolve first so duplicated tokens for the same SDK run only once,
        // and resolution failures become per-entry errors instead of aborting.
        var jobs = new List<(string Token, SdkInstallation? Sdk, string? Error)>();
        var seenVersions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawToken in tokens)
        {
            var token = Sanitize(rawToken);
            try
            {
                var sdk = await _execService.ResolveSdkAsync(token, cancellationToken).ConfigureAwait(false);
                if (seenVersions.Add(sdk.Version.Raw))
                {
                    jobs.Add((token, sdk, null));
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
            {
                jobs.Add((token, null, Sanitize(exception.Message)));
            }
        }

        var entries = new MatrixEntry[jobs.Count];
        async Task RunOneAsync(int index)
        {
            var (token, sdk, resolveError) = jobs[index];
            if (sdk is null)
            {
                entries[index] = new MatrixEntry(token, null, null, 0, string.Empty, string.Empty, resolveError);
                return;
            }

            var stopwatch = Stopwatch.StartNew();
            try
            {
                var result = await _execService
                    .ExecuteAsync(sdk.Version.Raw, command, captureOutput: true, cancellationToken)
                    .ConfigureAwait(false);
                stopwatch.Stop();
                entries[index] = new MatrixEntry(
                    token,
                    sdk.Version.Raw,
                    result.ExitCode,
                    stopwatch.ElapsedMilliseconds,
                    result.StandardOutput,
                    result.StandardError,
                    Error: null);
            }
            catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            {
                stopwatch.Stop();
                entries[index] = new MatrixEntry(
                    token,
                    sdk.Version.Raw,
                    null,
                    stopwatch.ElapsedMilliseconds,
                    string.Empty,
                    string.Empty,
                    Sanitize(exception.Message));
            }
        }

        if (parallel)
        {
            using var gate = new SemaphoreSlim(Math.Min(MaxParallelism, Environment.ProcessorCount));
            await Task.WhenAll(Enumerable.Range(0, jobs.Count).Select(async index =>
            {
                await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    await RunOneAsync(index).ConfigureAwait(false);
                }
                finally
                {
                    gate.Release();
                }
            })).ConfigureAwait(false);
        }
        else
        {
            for (var i = 0; i < jobs.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await RunOneAsync(i).ConfigureAwait(false);
            }
        }

        return new MatrixResult(entries);
    }

    private static string Sanitize(string value) =>
        new(value.Where(static c => !char.IsControl(c)).ToArray());
}
