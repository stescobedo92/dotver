using DotVer.Infrastructure;

namespace DotVer.Tests;

public sealed class SystemEnvironmentContextTests
{
    [Fact]
    public void EntryAssemblyPath_IsNeverEmpty()
    {
        var context = new SystemEnvironmentContext();

        Assert.False(string.IsNullOrWhiteSpace(context.EntryAssemblyPath));
        Assert.Equal(Path.GetFullPath(Environment.GetCommandLineArgs()[0]), context.EntryAssemblyPath);
    }
}
