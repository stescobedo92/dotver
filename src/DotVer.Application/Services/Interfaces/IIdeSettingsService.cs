using DotVer.Domain;

namespace DotVer.Application;

public interface IIdeSettingsService
{
    /// <summary>Points the C# extensions of VS Code at <paramref name="sdk"/> through .vscode/settings.json in <paramref name="projectDirectory"/>.</summary>
    IdeSettingsResult WriteVsCode(string projectDirectory, SdkInstallation sdk);
}
