using DotVer.Domain;

namespace DotVer.Application;

public interface IGlobalJsonService
{
    GlobalJsonRequest? TryFind(string workingDirectory);

    GlobalJsonRequest? TryRead(string filePath);

    GlobalJsonReadResult Read(string filePath);

    GlobalJsonWriteResult WritePin(string directory, SdkVersion version, SdkRollForward? rollForward);

    GlobalJsonWriteResult ClearPin(string workingDirectory, bool allowParent = false);
}
