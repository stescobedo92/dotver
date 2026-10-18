using System.Diagnostics;
using DotVer.Application;
using DotVer.Domain;

namespace DotVer.Infrastructure;

public sealed class ProcessRunner : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = spec.FileName,
            RedirectStandardError = !spec.InheritStandardIo,
            RedirectStandardOutput = !spec.InheritStandardIo,
            RedirectStandardInput = false,
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

        try
        {
            if (spec.InheritStandardIo)
            {
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                return new ProcessResult(process.ExitCode, string.Empty, string.Empty);
            }

            // Read both pipes concurrently: sequential reads deadlock when
            // the child fills the stderr pipe while we drain stdout.
            var stdoutTask = ReadStreamAsync(process.StandardOutput, spec.MaxCapturedCharsPerStream, cancellationToken);
            var stderrTask = ReadStreamAsync(process.StandardError, spec.MaxCapturedCharsPerStream, cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);

            return new ProcessResult(process.ExitCode, stdout, stderr);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
                // Process already exited between the check and Kill.
            }

            throw;
        }
    }

    private static async Task<string> ReadStreamAsync(TextReader reader, int? maxChars, CancellationToken cancellationToken)
    {
        if (maxChars is null)
        {
            return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        }

        var limit = Math.Max(0, maxChars.Value);
        var builder = new System.Text.StringBuilder();
        var chunk = new char[8192];
        var truncated = false;
        int read;
        while ((read = await reader.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            builder.Append(chunk, 0, read);
            if (builder.Length > limit)
            {
                truncated = true;
                builder.Remove(0, builder.Length - limit);
            }
        }

        var tail = builder.ToString();
        return truncated ? "[truncated]" + Environment.NewLine + tail : tail;
    }
}
