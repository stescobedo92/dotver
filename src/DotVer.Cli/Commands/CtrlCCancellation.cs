namespace DotVer.Cli;

/// <summary>Links Ctrl+C to a cancellation token for long-running commands.</summary>
internal sealed class CtrlCCancellation : IDisposable
{
    private readonly CancellationTokenSource _source = new();
    private readonly ConsoleCancelEventHandler _handler;

    private int _presses;

    private CtrlCCancellation()
    {
        _handler = (_, args) =>
        {
            // First Ctrl+C asks cooperatively; a second one terminates, so a
            // blocked operation can never trap the user.
            if (Interlocked.Increment(ref _presses) == 1)
            {
                args.Cancel = true;
                _source.Cancel();
            }
        };
        global::System.Console.CancelKeyPress += _handler;
    }

    public CancellationToken Token => _source.Token;

    public static CtrlCCancellation Create() => new();

    public void Dispose()
    {
        global::System.Console.CancelKeyPress -= _handler;
        _source.Dispose();
    }
}
