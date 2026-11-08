using System.CommandLine;
using Abox.Infrastructure;
using Abox.Sbx;

namespace Abox.Cli;

/// <summary>
/// <c>abox pipe</c>: runs a command in a sandbox with the standard input and output
/// attached, and prints nothing else. The Herdr app calls it to reach a sandbox.
/// It is not a user command and stays out of the help.
/// </summary>
public static class PipeCommand
{
    public static Command Create(ISbxClient sbx, TrailingArguments trailing, IConsole console)
    {
        var sandbox = new Argument<string>("sandbox")
        {
            Description = "The sandbox to run the command in.",
        };

        var command = new Command(
            "pipe",
            "Run a command in a sandbox, with the standard streams attached."
        )
        {
            Arguments = { sandbox },
            Hidden = true,
        };

        command.SetAction(
            async (parseResult, cancellationToken) =>
            {
                var argv = trailing.Values;
                if (argv.Count == 0)
                {
                    console.WriteError(
                        "error: no command given. Use: abox pipe <sandbox> -- <command>"
                    );
                    return 1;
                }

                return await CommandHandling.RunAsync(
                    async () =>
                        await sbx.ExecAsync(
                            new SbxExecRequest(
                                parseResult.GetValue(sandbox)!,
                                argv,
                                KeepStandardInputOpen: true
                            ),
                            cancellationToken
                        ),
                    console
                );
            }
        );

        return command;
    }
}
