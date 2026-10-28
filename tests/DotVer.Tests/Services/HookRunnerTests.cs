using DotVer.Application;
using DotVer.Domain;

namespace DotVer.Tests;

public sealed class HookRunnerTests
{
    [Fact]
    public async Task Run_WithoutHook_DoesNothing()
    {
        using var sandbox = new TestSandbox(TestPlatform.Linux);
        var sdk = sandbox.CreateSdkInstall("8.0.412", isManaged: true);
        var environment = sandbox.CreateEnvironment();
        var runner = new FakeProcessRunner();
        var hooks = new HookRunner(DotVerPaths.From(environment), environment, runner);

        var result = await hooks.RunAsync("post-install", sdk, CancellationToken.None);

        Assert.False(result.Ran);
        Assert.Empty(runner.Invocations);
    }

    [Fact]
    public async Task Run_UnixHook_IsExecutedDirectlyWithTheSdkEnvironment()
    {
        using var sandbox = new TestSandbox(TestPlatform.Linux);
        var sdk = sandbox.CreateSdkInstall("8.0.412", isManaged: true);
        var environment = sandbox.CreateEnvironment();
        var paths = DotVerPaths.From(environment);
        var hook = Path.Combine(Directory.CreateDirectory(Path.Combine(paths.Home, "hooks")).FullName, "post-install");
        File.WriteAllText(hook, "#!/bin/sh\necho hi\n");
        var runner = new FakeProcessRunner();
        var hooks = new HookRunner(paths, environment, runner);

        var result = await hooks.RunAsync("post-install", sdk, CancellationToken.None);

        Assert.True(result.Ran);
        Assert.Equal(0, result.ExitCode);
        var spec = Assert.Single(runner.Invocations);
        Assert.Equal(hook, spec.FileName);
        Assert.Equal("post-install", spec.EnvironmentVariables?["DOTVER_EVENT"]);
        Assert.Equal("8.0.412", spec.EnvironmentVariables?["DOTVER_VERSION"]);
        Assert.Equal(sdk.HostRoot, spec.EnvironmentVariables?["DOTNET_ROOT"]);
        Assert.Equal(paths.Home, spec.EnvironmentVariables?["DOTVER_HOME"]);
    }

    [Fact]
    public async Task Run_WindowsCmdHook_GoesThroughComSpec()
    {
        using var sandbox = new TestSandbox(TestPlatform.Windows);
        var sdk = sandbox.CreateSdkInstall("8.0.412", isManaged: true);
        var environment = sandbox.CreateEnvironment();
        var comSpec = Path.Combine(sandbox.CreateDirectory("System32"), "cmd.exe");
        File.WriteAllText(comSpec, string.Empty);
        environment.SetUserEnvironmentVariable("ComSpec", comSpec);
        var paths = DotVerPaths.From(environment);
        var hook = Path.Combine(Directory.CreateDirectory(Path.Combine(paths.Home, "hooks")).FullName, "post-install.cmd");
        File.WriteAllText(hook, "@echo hi\r\n");
        var runner = new FakeProcessRunner();
        var hooks = new HookRunner(paths, environment, runner);

        var result = await hooks.RunAsync("post-install", sdk, CancellationToken.None);

        Assert.True(result.Ran);
        var spec = Assert.Single(runner.Invocations);
        Assert.Equal(comSpec, spec.FileName);
        Assert.Equal(["/d", "/c", hook], spec.Arguments);
    }

    [Fact]
    public async Task Run_FailingHook_IsReportedNotThrown()
    {
        using var sandbox = new TestSandbox(TestPlatform.Linux);
        var sdk = sandbox.CreateSdkInstall("8.0.412", isManaged: true);
        var environment = sandbox.CreateEnvironment();
        var paths = DotVerPaths.From(environment);
        var hook = Path.Combine(Directory.CreateDirectory(Path.Combine(paths.Home, "hooks")).FullName, "post-install");
        File.WriteAllText(hook, "exit 3");
        var runner = new FakeProcessRunner(_ => new ProcessResult(3, string.Empty, "boom"));
        var hooks = new HookRunner(paths, environment, runner);

        var result = await hooks.RunAsync("post-install", sdk, CancellationToken.None);

        Assert.True(result.Ran);
        Assert.Equal(3, result.ExitCode);
        Assert.Contains("boom", result.Output, StringComparison.Ordinal);
    }
}
