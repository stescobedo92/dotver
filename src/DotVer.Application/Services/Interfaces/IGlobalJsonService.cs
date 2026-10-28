using DotVer.Domain;

namespace DotVer.Application;

public interface IGlobalJsonService
{
    GlobalJsonRequest? TryFind(string workingDirectory);

    /// <summary>Path of the nearest global.json at or above <paramref name="workingDirectory"/>, whether or not it holds a pin.</summary>
    string? FindNearest(string workingDirectory);

    GlobalJsonRequest? TryRead(string filePath);

    GlobalJsonReadResult Read(string filePath);

    GlobalJsonWriteResult WritePin(string directory, SdkVersion version, SdkRollForward? rollForward);

    GlobalJsonWriteResult ClearPin(string workingDirectory, bool allowParent = false);
}
