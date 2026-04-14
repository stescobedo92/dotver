using DotVer.Application;
using DotVer.Domain;

namespace DotVer.Tests;

internal sealed class InMemoryConfigStore : IConfigStore
{
    private DotVerConfig _config;

    public InMemoryConfigStore(DotVerConfig config)
    {
        _config = config;
    }

    public Task<DotVerConfig> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(_config);

    public Task SaveAsync(DotVerConfig config, CancellationToken cancellationToken)
    {
        _config = config;
        return Task.CompletedTask;
    }
}

internal sealed class FakeProcessRunner : IProcessRunner
{
    public List<ProcessSpec> Invocations { get; } = [];

    public Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken cancellationToken)
    {
        Invocations.Add(spec);
        return Task.FromResult(new ProcessResult(0, "fake-output", string.Empty));
    }
}

public enum TestPlatform
{
    Windows,
    Linux,
    MacOS
}
