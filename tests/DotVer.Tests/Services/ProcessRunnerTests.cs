using System.Runtime.InteropServices;
using DotVer.Domain;
using DotVer.Infrastructure;

namespace DotVer.Tests;

public sealed class ProcessRunnerTests
{
    [Fact]
    public async Task CaptureMode_ReturnsOutputAndExitCode()
    {
        var runner = new ProcessRunner();

        var result = await runner.RunAsync(new ProcessSpec(DotnetHost(), ["--version"]), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.True(Version.TryParse(result.StandardOutput.Trim().Split('-')[0], out _));
    }

    [Fact]
    public async Task CaptureMode_WithLimit_KeepsBoundedTailWithMarker()
    {
        var runner = new ProcessRunner();

        var result = await runner.RunAsync(
            new ProcessSpec(DotnetHost(), ["--info"], MaxCapturedCharsPerStream: 64),
            CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.StartsWith("[truncated]", result.StandardOutput);
        Assert.True(result.StandardOutput.Length <= 64 + "[truncated]".Length + Environment.NewLine.Length);
    }

    [Fact]
    public async Task InheritMode_ReturnsExitCodeWithoutCapturedOutput()
    {
        var runner = new ProcessRunner();

        var result = await runner.RunAsync(
            new ProcessSpec(DotnetHost(), ["--version"], InheritStandardIo: true),
            CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, result.StandardOutput);
        Assert.Equal(string.Empty, result.StandardError);
    }

    [Theory]
    [InlineData("dotnet")]
    [InlineData("./dotnet")]
    public async Task RunAsync_RejectsFileNamesThatAreNotFullyQualified(string fileName)
    {
        var runner = new ProcessRunner();

        // A bare or relative name lets CreateProcess/execvp resolve through the
        // current directory, which may be an untrusted repository.
        await Assert.ThrowsAsync<ArgumentException>(
            () => runner.RunAsync(new ProcessSpec(fileName, ["--version"]), CancellationToken.None));
    }

    private static string DotnetHost()
    {
        // <root>/shared/Microsoft.NETCore.App/<version> -> walk up to <root>/dotnet(.exe)
        var fileName = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
        var cursor = new DirectoryInfo(RuntimeEnvironment.GetRuntimeDirectory());
        while (cursor is not null)
        {
            var candidate = Path.Combine(cursor.FullName, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            cursor = cursor.Parent;
        }

        throw new InvalidOperationException("Could not locate the dotnet host from the runtime directory.");
    }
}
