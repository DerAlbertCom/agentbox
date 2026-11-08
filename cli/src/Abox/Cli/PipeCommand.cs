using System.CommandLine;
using Abox.Sbx;

namespace Abox.Cli;

/// <summary>
/// <c>abox pipe</c>: runs a command in a sandbox with the standard input and output
/// attached, and prints nothing else. The Herdr app calls it to reach a sandbox.
/// It is not a user command and stays out of the help.
/// </summary>
public static class PipeCommand
{
    public static Command Create(AboxServices services)
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
            (Func<ParseResult, CancellationToken, Task<int>>)(
                async (parseResult, cancellationToken) =>
                {
                    var argv = services.Trailing.Values;
                    if (argv.Count == 0)
                    {
                        services.Console.WriteError(
                            "error: no command given. Use: abox pipe <sandbox> -- <command>"
                        );
                        return 1;
                    }

                    return await CommandHandling.RunAsync(
                        async () =>
                        {
                            var exitCode = await services.Sbx.ExecAsync(
                                new SbxExecRequest(
                                    parseResult.GetValue(sandbox)!,
                                    argv,
                                    KeepStandardInputOpen: true
                                ),
                                cancellationToken
                            );

                            return exitCode;
                        },
                        services.Console
                    );
                }
            )
        );

        return command;
    }
}
