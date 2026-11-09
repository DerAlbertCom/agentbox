using System.CommandLine;
using Abox.Infrastructure;

namespace Abox.Cli;

/// <summary>
/// The abox command line: the commands the app registered, one per slice.
/// </summary>
public sealed class AboxRootCommand : RootCommand
{
    private readonly ProcessOutput _output;
    private readonly Option<bool> _verbose = new("--verbose")
    {
        Description =
            "Show complete captured tool stdout and stderr on stderr (with known secrets redacted).",
        Recursive = true,
    };

    public AboxRootCommand(IEnumerable<Command> subcommands, ProcessOutput output)
        : base("abox: AI agent sandboxes for your folders.")
    {
        _output = output;
        Options.Add(_verbose);
        foreach (var subcommand in subcommands)
        {
            Subcommands.Add(subcommand);
        }
    }

    public Task<int> InvokeAsync(IReadOnlyList<string> arguments)
    {
        var parsed = this.Parse(arguments);
        _output.Verbose = parsed.GetValue(_verbose);
        return parsed.InvokeAsync();
    }
}
