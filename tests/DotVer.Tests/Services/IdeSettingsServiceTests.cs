using System.Text.Json;
using DotVer.Application;

namespace DotVer.Tests;

public sealed class IdeSettingsServiceTests
{
    [Fact]
    public void WriteVsCode_CreatesSettingsWithBothDotnetPathKeys()
    {
        using var sandbox = new TestSandbox(TestPlatform.Windows);
        var sdk = sandbox.CreateSdkInstall("8.0.412", isManaged: true);
        var project = sandbox.CreateDirectory("project");

        var result = new IdeSettingsService().WriteVsCode(project, sdk);

        Assert.True(result.Created);
        Assert.Equal(Path.Combine(project, ".vscode", "settings.json"), result.FilePath);
        using var doc = JsonDocument.Parse(File.ReadAllText(result.FilePath));
        Assert.Equal(sdk.HostRoot, doc.RootElement.GetProperty("dotnet.dotnetPath").GetString());
        Assert.Equal(sdk.HostRoot, doc.RootElement.GetProperty("omnisharp.dotnetPath").GetString());
        Assert.Equal(["dotnet.dotnetPath", "omnisharp.dotnetPath"], result.KeysWritten);
    }

    [Fact]
    public void WriteVsCode_MergesIntoExistingSettingsAndKeepsOtherKeys()
    {
        using var sandbox = new TestSandbox(TestPlatform.Windows);
        var sdk = sandbox.CreateSdkInstall("8.0.412", isManaged: true);
        var project = sandbox.CreateDirectory("project");
        var vscode = Directory.CreateDirectory(Path.Combine(project, ".vscode")).FullName;
        // VS Code writes JSONC: comments and trailing commas must not break the merge.
        File.WriteAllText(Path.Combine(vscode, "settings.json"), """
            {
              // keep me
              "editor.formatOnSave": true,
              "dotnet.dotnetPath": "C:/old/dotnet",
            }
            """);

        var result = new IdeSettingsService().WriteVsCode(project, sdk);

        Assert.False(result.Created);
        Assert.True(result.CommentsDropped);
        using var doc = JsonDocument.Parse(File.ReadAllText(result.FilePath));
        Assert.True(doc.RootElement.GetProperty("editor.formatOnSave").GetBoolean());
        Assert.Equal(sdk.HostRoot, doc.RootElement.GetProperty("dotnet.dotnetPath").GetString());
        Assert.Empty(Directory.EnumerateFiles(vscode, "*.tmp"));
    }

    [Fact]
    public void WriteVsCode_RefusesSymlinkedSettingsFile()
    {
        using var sandbox = new TestSandbox(TestPlatform.Windows);
        var sdk = sandbox.CreateSdkInstall("8.0.412", isManaged: true);
        var project = sandbox.CreateDirectory("project");
        var vscode = Directory.CreateDirectory(Path.Combine(project, ".vscode")).FullName;
        var target = Path.Combine(sandbox.CreateDirectory("elsewhere"), "secret.json");
        File.WriteAllText(target, """{"token":"s3cr3t"}""");
        if (!SymlinkSupport.TryCreateFile(Path.Combine(vscode, "settings.json"), target))
        {
            return; // symlink creation needs a privilege this runner lacks
        }

        Assert.Throws<InvalidOperationException>(() => new IdeSettingsService().WriteVsCode(project, sdk));

        Assert.Equal("""{"token":"s3cr3t"}""", File.ReadAllText(target));
        Assert.NotNull(new FileInfo(Path.Combine(vscode, "settings.json")).LinkTarget);
    }

    [Fact]
    public void WriteVsCode_RefusesSettingsThatAreNotAnObject()
    {
        using var sandbox = new TestSandbox(TestPlatform.Windows);
        var sdk = sandbox.CreateSdkInstall("8.0.412", isManaged: true);
        var project = sandbox.CreateDirectory("project");
        var vscode = Directory.CreateDirectory(Path.Combine(project, ".vscode")).FullName;
        File.WriteAllText(Path.Combine(vscode, "settings.json"), "[1, 2]");

        Assert.Throws<InvalidOperationException>(() => new IdeSettingsService().WriteVsCode(project, sdk));

        Assert.Equal("[1, 2]", File.ReadAllText(Path.Combine(vscode, "settings.json")));
    }
}
