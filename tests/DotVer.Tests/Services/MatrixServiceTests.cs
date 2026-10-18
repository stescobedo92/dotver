using DotVer.Application;
using DotVer.Domain;

namespace DotVer.Tests;

public sealed class MatrixServiceTests
{
    [Fact]
    public async Task Run_ResolvesDedupesAndAggregatesFailures()
    {
        var exec = new FakeExec(new Dictionary<string, ProcessResult>
        {
            ["8.0.412"] = new(0, "ok8", string.Empty),
            ["9.0.100"] = new(1, "out9", "err9")
        });
        var service = new MatrixService(exec);

        var result = await service.RunAsync(["8.0", "8.0.412", "9.0", "missing"], ["dotnet", "--version"], parallel: false, CancellationToken.None);

        Assert.False(result.AllSucceeded);
        Assert.Equal(3, result.Entries.Count);
        Assert.Equal(0, result.Entries[0].ExitCode);
        Assert.Equal("ok8", result.Entries[0].StandardOutput);
        Assert.Equal(1, result.Entries[1].ExitCode);
        Assert.Equal("err9", result.Entries[1].StandardError);
        Assert.NotNull(result.Entries[2].Error);
        Assert.Equal(2, exec.Executions);
    }

    [Fact]
    public async Task Run_Parallel_PreservesTokenOrder()
    {
        var exec = new FakeExec(new Dictionary<string, ProcessResult>
        {
            ["8.0.412"] = new(0, "a", string.Empty),
            ["9.0.100"] = new(0, "b", string.Empty)
        });
        var service = new MatrixService(exec);

        var result = await service.RunAsync(["9.0", "8.0"], ["dotnet", "--version"], parallel: true, CancellationToken.None);

        Assert.True(result.AllSucceeded);
        Assert.Equal(["9.0.100", "8.0.412"], result.Entries.Select(e => e.Version).ToList());
    }

    [Fact]
    public async Task Run_TooManyTokens_Throws()
    {
        var service = new MatrixService(new FakeExec(new Dictionary<string, ProcessResult>()));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.RunAsync(Enumerable.Range(0, 17).Select(i => i.ToString()).ToList(), ["dotnet"], false, CancellationToken.None));
    }

    private sealed class FakeExec(IReadOnlyDictionary<string, ProcessResult> results) : IExecService
    {
        public int Executions { get; private set; }

        public Task<SdkInstallation> ResolveSdkAsync(string token, CancellationToken cancellationToken)
        {
            var match = results.Keys.FirstOrDefault(raw => raw.StartsWith(token, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                throw new InvalidOperationException($"No selectable version found for '{token}'.");
            }

            var root = Path.Combine(Path.GetTempPath(), "dotver-matrix-test", match);
            return Task.FromResult(new SdkInstallation(
                SdkVersion.Parse(match),
                root,
                Path.Combine(root, "sdk", match),
                Path.Combine(root, "dotnet"),
                SdkOriginKind.ManagedInstall,
                "test",
                true,
                true,
                "test"));
        }

        public Task<ProcessResult> ExecuteAsync(string token, IReadOnlyList<string> command, bool captureOutput, CancellationToken cancellationToken)
        {
            Executions++;
            Assert.True(captureOutput);
            return Task.FromResult(results[token]);
        }
    }
}
