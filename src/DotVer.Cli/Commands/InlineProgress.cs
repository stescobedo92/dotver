namespace DotVer.Cli;

/// <summary>
/// Reports synchronously on the calling thread. <see cref="Progress{T}"/>
/// would post through a synchronization context the CLI does not have, so
/// its callbacks could run after the work they describe has finished.
/// </summary>
internal sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}
