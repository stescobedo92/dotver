using DotVer.Domain;

namespace DotVer.Application;

public interface IGlobalJsonService
{
    GlobalJsonRequest? TryFind(string workingDirectory);
}
