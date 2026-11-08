using System.CommandLine;

namespace Abox.Cli;

/// <summary>
/// The abox command line.
/// </summary>
public static class AboxCommand
{
    public static RootCommand Create(AboxServices services)
    {
        var root = new RootCommand("abox: AI agent sandboxes for your folders.")
        {
            Subcommands =
            {
                BuildCommand.Create(services),
                CreateCommand.Create(services),
                RunCommand.Create(services),
                SecretsCommand.Create(services),
                PipeCommand.Create(services),
                ExecCommand.Create(services),
            },
        };

        return root;
    }
}
