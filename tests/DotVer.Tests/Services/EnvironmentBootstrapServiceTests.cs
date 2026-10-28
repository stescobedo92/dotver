using DotVer.Application;

namespace DotVer.Tests;

public sealed class EnvironmentBootstrapServiceTests
{
    [Fact]
    public async Task NoBootstrapVariable_LeavesProfilesPathAndUserVariablesUntouched()
    {
        using var sandbox = new TestSandbox(TestPlatform.Linux);
        var environment = sandbox.CreateEnvironment();
        environment.SetUserEnvironmentVariable("DOTVER_NO_BOOTSTRAP", "1");
        var writesBefore = environment.UserVariableWrites;
        var paths = DotVerPaths.From(environment);
        var bootstrap = new EnvironmentBootstrapService(paths, environment, new ShimService(paths, environment));

        var result = await bootstrap.EnsureConfiguredAsync(CancellationToken.None);

        Assert.Empty(result.ProfilesUpdated);
        Assert.False(result.PersistentPathUpdated);
        Assert.False(result.ProcessPathUpdated);
        Assert.Contains(result.Notes, note => note.Contains("DOTVER_NO_BOOTSTRAP", StringComparison.Ordinal));
        Assert.False(File.Exists(Path.Combine(environment.HomeDirectory, ".bashrc")));
        Assert.False(File.Exists(Path.Combine(environment.HomeDirectory, ".profile")));
        Assert.Equal(writesBefore, environment.UserVariableWrites);
        // Shims live inside DOTVER_HOME, which is the directory dotver owns.
        Assert.True(File.Exists(Path.Combine(paths.Shims, "dotnet")));
    }

    [Fact]
    public async Task UnixBootstrap_DoesNotReAddSnippetTheUserRemoved()
    {
        using var sandbox = new TestSandbox(TestPlatform.Linux);
        var environment = sandbox.CreateEnvironment();
        var paths = DotVerPaths.From(environment);
        var bootstrap = new EnvironmentBootstrapService(paths, environment, new ShimService(paths, environment));
        await bootstrap.EnsureConfiguredAsync(CancellationToken.None);

        var bashrc = Path.Combine(environment.HomeDirectory, ".bashrc");
        const string userContent = "# my bashrc, dotver block removed on purpose\n";
        File.WriteAllText(bashrc, userContent);

        var second = await bootstrap.EnsureConfiguredAsync(CancellationToken.None);

        Assert.Equal(userContent, File.ReadAllText(bashrc));
        Assert.Empty(second.ProfilesUpdated);
    }

    [Fact]
    public async Task UnixBootstrap_WritesThroughSymlinkedProfileAndKeepsTheLink()
    {
        using var sandbox = new TestSandbox(TestPlatform.Linux);
        var environment = sandbox.CreateEnvironment();
        var dotfiles = Directory.CreateDirectory(Path.Combine(environment.HomeDirectory, "dotfiles")).FullName;
        var realBashrc = Path.Combine(dotfiles, "bashrc");
        File.WriteAllText(realBashrc, "# managed by my dotfiles repo\n");
        var bashrc = Path.Combine(environment.HomeDirectory, ".bashrc");
        if (!SymlinkSupport.TryCreateFile(bashrc, realBashrc))
        {
            return; // symlink creation needs a privilege this runner lacks
        }

        var paths = DotVerPaths.From(environment);
        var bootstrap = new EnvironmentBootstrapService(paths, environment, new ShimService(paths, environment));

        await bootstrap.EnsureConfiguredAsync(CancellationToken.None);

        Assert.NotNull(new FileInfo(bashrc).LinkTarget);
        var content = File.ReadAllText(realBashrc);
        Assert.StartsWith("# managed by my dotfiles repo", content, StringComparison.Ordinal);
        Assert.Contains("dotver.sh", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnixBootstrap_SkipsFishWhenItsConfigDirectoryDoesNotExist()
    {
        using var sandbox = new TestSandbox(TestPlatform.Linux);
        var environment = sandbox.CreateEnvironment();
        var paths = DotVerPaths.From(environment);
        var bootstrap = new EnvironmentBootstrapService(paths, environment, new ShimService(paths, environment));

        var result = await bootstrap.EnsureConfiguredAsync(CancellationToken.None);

        Assert.DoesNotContain(result.ProfilesUpdated, profile => profile.EndsWith("config.fish", StringComparison.Ordinal));
        Assert.False(Directory.Exists(Path.Combine(environment.HomeDirectory, ".config", "fish")));
    }

    [Fact]
    public async Task WindowsBootstrap_SecondRunWritesNoUserVariables()
    {
        using var sandbox = new TestSandbox(TestPlatform.Windows);
        var environment = sandbox.CreateEnvironment();
        var paths = DotVerPaths.From(environment);
        var bootstrap = new EnvironmentBootstrapService(paths, environment, new ShimService(paths, environment));

        await bootstrap.EnsureConfiguredAsync(CancellationToken.None);
        var writesAfterFirst = environment.UserVariableWrites;
        var second = await bootstrap.EnsureConfiguredAsync(CancellationToken.None);

        Assert.Equal(writesAfterFirst, environment.UserVariableWrites);
        Assert.False(second.PersistentPathUpdated);
        Assert.Empty(second.ProfilesUpdated);
    }

    [Fact]
    public async Task WindowsBootstrap_RecognizesShimsBehindAnUnexpandedPathEntry()
    {
        using var sandbox = new TestSandbox(TestPlatform.Windows);
        var environment = sandbox.CreateEnvironment();
        var paths = DotVerPaths.From(environment);
        var variable = "DOTVER_TEST_SHIMS_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(variable, paths.Shims);
        try
        {
            // What a REG_EXPAND_SZ user PATH looks like when read unexpanded.
            // The service splits with the host separator, so the test must too.
            var rawPath = "%" + variable + "%" + Path.PathSeparator + @"C:\other";
            environment.SetUserEnvironmentVariable("PATH", rawPath);
            var bootstrap = new EnvironmentBootstrapService(paths, environment, new ShimService(paths, environment));

            await bootstrap.EnsureConfiguredAsync(CancellationToken.None);

            Assert.Equal(rawPath, environment.GetUserEnvironmentVariable("PATH"));
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }
}
