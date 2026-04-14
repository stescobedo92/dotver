using Spectre.Console;
using Spectre.Console.Cli;

namespace DotVer.Cli;

internal abstract class AsyncCommandBase<TSettings> : AsyncCommand<TSettings>
    where TSettings : CommandSettings
{
    protected readonly IAnsiConsole Console;

    protected AsyncCommandBase(IAnsiConsole console)
    {
        Console = console;
    }

    protected static string Flag(bool value) => value ? "[green]yes[/]" : "[red]no[/]";
}

internal abstract class AsyncCommandBase : AsyncCommand
{
    protected readonly IAnsiConsole Console;

    protected AsyncCommandBase(IAnsiConsole console)
    {
        Console = console;
    }

    protected static string Flag(bool value) => value ? "[green]yes[/]" : "[red]no[/]";
}
