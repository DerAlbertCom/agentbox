using System.CommandLine;
using Abox.Cli;
using Abox.Infrastructure;
using Abox.Sbx;

namespace Abox.Features.Sandbox;

/// <summary>
/// <c>abox exec</c>: runs a command in a sandbox, starting the sandbox when it is stopped.
/// It is not a user command and stays out of the help.
/// </summary>
public sealed class ExecCommand : Command
{
    public ExecCommand(ISbxClient sbx, TrailingArguments trailing, IConsole console)
        : base("exec", "Run a command in a sandbox.")
    {
        Hidden = true;

        var sandbox = new Argument<string>("sandbox")
        {
            Description = "The sandbox to run the command in.",
        };

        Arguments.Add(sandbox);

        SetAction(
            async (parseResult, cancellationToken) =>
            {
                var argv = trailing.Values;
                if (argv.Count == 0)
                {
                    console.WriteError(
                        "error: no command given. Use: abox exec <sandbox> -- <command>"
                    );
                    return 1;
                }

                return await CommandHandling.RunAsync(
                    async () =>
                        await sbx.ExecAsync(
                            new SbxExecRequest(parseResult.GetValue(sandbox)!, argv),
                            cancellationToken
                        ),
                    console
                );
            }
        );
    }
}
