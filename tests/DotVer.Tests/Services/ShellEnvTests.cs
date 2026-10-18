using DotVer.Application;
using DotVer.Domain;

namespace DotVer.Tests;

public sealed class ShellEnvTests
{
    [Fact]
    public void EnvScript_Bash_EscapesSingleQuotesAndDollar()
    {
        var sdk = Sdk("/opt/evil'$(touch-pwned)/8.0.412");

        var script = EnvScript.Render(ShellKind.Bash, sdk);

        Assert.Contains("'/opt/evil'\\''$(touch-pwned)/8.0.412'", script, StringComparison.Ordinal);
        Assert.Contains("export DOTVER_VERSION='8.0.412'", script, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ShellKind.Bash)]
    [InlineData(ShellKind.Cmd)]
    [InlineData(ShellKind.PowerShell)]
    [InlineData(ShellKind.Fish)]
    public void EnvScript_WithControlCharacters_ThrowsInEveryDialect(ShellKind shell)
    {
        var sdk = Sdk("/opt/evil\ntouch-pwned/8.0.412");

        Assert.Throws<ArgumentException>(() => EnvScript.Render(shell, sdk));
    }

    [Fact]
    public void EnvScript_Cmd_WithExclamation_Throws()
    {
        var sdk = Sdk("C:\\tools\\bang!dir\\8.0.412");

        Assert.Throws<ArgumentException>(() => EnvScript.Render(ShellKind.Cmd, sdk));
        Assert.Contains("bang!dir", EnvScript.Render(ShellKind.Bash, sdk), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SpawnShell_RelativePathOverride_ThrowsWithoutRunning()
    {
        using var sandbox = new TestSandbox(TestPlatform.Windows);
        var sdk = sandbox.CreateSdkInstall("8.0.412", isManaged: true);
        var environment = sandbox.CreateEnvironment();
        var process = new RecordingProcessRunner(new ProcessResult(0, string.Empty, string.Empty));
        var service = new ShellService(new FakeExec(sdk), process, environment);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SpawnShellAsync("8.0", ".\\pwsh.exe", CancellationToken.None));
        Assert.Empty(process.Invocations);
    }

    [Fact]
    public void EnvScript_PowerShell_UsesSingleQuotedLiterals()
    {
        var sdk = Sdk("C:\\tools\\o'brien$home\\8.0.412");

        var script = EnvScript.Render(ShellKind.PowerShell, sdk);

        Assert.Contains("$env:DOTNET_ROOT = 'C:\\tools\\o''brien$home\\8.0.412'", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SpawnShell_ExplicitPath_SetsPinnedEnvironmentAndInheritsIo()
    {
        using var sandbox = new TestSandbox(TestPlatform.Windows);
        var sdk = sandbox.CreateSdkInstall("8.0.412", isManaged: true);
        var environment = sandbox.CreateEnvironment();
        var process = new RecordingProcessRunner(new ProcessResult(3, string.Empty, string.Empty));
        var shellExe = Path.Combine(sandbox.Root, "pwsh.exe");
        File.WriteAllText(shellExe, string.Empty);
        var service = new ShellService(new FakeExec(sdk), process, environment);

        var result = await service.SpawnShellAsync("8.0", shellExe, CancellationToken.None);

        Assert.Equal(3, result.ExitCode);
        var spec = Assert.Single(process.Invocations);
        Assert.Equal(shellExe, spec.FileName);
        Assert.Equal(["-NoLogo"], spec.Arguments);
        Assert.True(spec.InheritStandardIo);
        Assert.Equal(sdk.HostRoot, spec.EnvironmentVariables!["DOTNET_ROOT"]);
        Assert.Equal("8.0.412", spec.EnvironmentVariables["DOTVER_VERSION"]);
    }

    [Fact]
    public async Task SpawnShell_UnknownBareShell_ThrowsWithoutRunning()
    {
        using var sandbox = new TestSandbox(TestPlatform.Windows);
        var sdk = sandbox.CreateSdkInstall("8.0.412", isManaged: true);
        var environment = sandbox.CreateEnvironment();
        var process = new RecordingProcessRunner(new ProcessResult(0, string.Empty, string.Empty));
        var service = new ShellService(new FakeExec(sdk), process, environment);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SpawnShellAsync("8.0", "definitely-not-a-shell-xyz", CancellationToken.None));
        Assert.Empty(process.Invocations);
    }

    private static SdkInstallation Sdk(string hostRoot) => new(
        SdkVersion.Parse("8.0.412"),
        hostRoot,
        Path.Combine(hostRoot, "sdk", "8.0.412"),
        Path.Combine(hostRoot, "dotnet"),
        SdkOriginKind.ManagedInstall,
        "test",
        true,
        true,
        "test");

    private sealed class FakeExec(SdkInstallation sdk) : IExecService
    {
        public Task<SdkInstallation> ResolveSdkAsync(string token, CancellationToken cancellationToken) =>
            Task.FromResult(sdk);

        public Task<ProcessResult> ExecuteAsync(string token, IReadOnlyList<string> command, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingProcessRunner(ProcessResult result) : IProcessRunner
    {
        public List<ProcessSpec> Invocations { get; } = [];

        public Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken cancellationToken)
        {
            Invocations.Add(spec);
            return Task.FromResult(result);
        }
    }
}
