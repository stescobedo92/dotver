using System.Text.Json;
using System.Text.Json.Nodes;
using DotVer.Domain;

namespace DotVer.Application;

/// <summary>
/// Shims only reach terminals; editors start from the desktop and use the
/// system dotnet. VS Code (C# Dev Kit and OmniSharp) reads the SDK host
/// directory from settings.json, so that is what gets written here.
/// </summary>
public sealed class IdeSettingsService : IIdeSettingsService
{
    private const long MaxSettingsBytes = 1024 * 1024;

    // VS Code writes JSONC; the strict pass only detects comments so the user
    // can be told they were not preserved (JsonNode cannot round-trip them).
    private static readonly JsonDocumentOptions LenientOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly JsonDocumentOptions StrictOptions = new()
    {
        CommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = true
    };

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private static readonly string[] Keys = ["dotnet.dotnetPath", "omnisharp.dotnetPath"];

    public IdeSettingsResult WriteVsCode(string projectDirectory, SdkInstallation sdk)
    {
        ArgumentNullException.ThrowIfNull(sdk);
        var directory = Path.GetFullPath(projectDirectory);
        if (!Directory.Exists(directory))
        {
            throw new InvalidOperationException($"Directory '{TerminalText.Sanitize(directory)}' does not exist.");
        }

        var vscodeDirectory = Path.Combine(directory, ".vscode");
        var target = Path.Combine(vscodeDirectory, "settings.json");

        var root = new JsonObject();
        var created = true;
        var commentsDropped = false;
        if (File.Exists(target) || new FileInfo(target).LinkTarget is not null)
        {
            created = false;
            root = ReadExisting(target, out commentsDropped);
        }

        foreach (var key in Keys)
        {
            root[key] = sdk.HostRoot;
        }

        Directory.CreateDirectory(vscodeDirectory);
        GlobalJsonService.WriteAtomically(target, root.ToJsonString(WriteOptions) + Environment.NewLine);
        return new IdeSettingsResult(target, created, Keys, commentsDropped, sdk.HostRoot);
    }

    private static JsonObject ReadExisting(string target, out bool commentsDropped)
    {
        // The project directory may be a cloned repository: never follow a
        // link and never read more than a settings file could reasonably be.
        byte[]? bytes;
        using (var stream = GlobalJsonService.OpenRegularFile(target)
                            ?? throw new InvalidOperationException($"'{TerminalText.Sanitize(target)}' is a symbolic link or not a regular file; refusing to modify it."))
        {
            bytes = GlobalJsonService.ReadBounded(stream, MaxSettingsBytes);
        }

        if (bytes is null)
        {
            throw new InvalidOperationException($"'{TerminalText.Sanitize(target)}' exceeds the {MaxSettingsBytes}-byte limit; refusing to modify it.");
        }

        var payload = GlobalJsonService.StripUtf8Bom(bytes);
        commentsDropped = false;
        try
        {
            using var strict = JsonDocument.Parse(payload, StrictOptions);
        }
        catch (JsonException)
        {
            commentsDropped = true;
        }

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(payload.Span, documentOptions: LenientOptions);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"'{TerminalText.Sanitize(target)}' is not valid JSON; refusing to overwrite it. {exception.Message}", exception);
        }

        return node as JsonObject
            ?? throw new InvalidOperationException($"'{TerminalText.Sanitize(target)}' does not contain a JSON object; refusing to overwrite it.");
    }
}
