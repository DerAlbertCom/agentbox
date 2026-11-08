using System.CommandLine;

namespace Abox.Cli;

/// <summary>
/// The abox command line: the commands the app registered, one per slice.
/// </summary>
public sealed class AboxRootCommand : RootCommand
{
    public AboxRootCommand(IEnumerable<Command> subcommands)
        : base("abox: AI agent sandboxes for your folders.")
    {
        foreach (var subcommand in subcommands)
        {
            Subcommands.Add(subcommand);
        }
    }
}
