using System.CommandLine;
using Abox.Sbx;

namespace Abox.Cli;

/// <summary>
/// <c>abox exec</c>: runs a command in a sandbox, starting the sandbox when it is stopped.
/// It is not a user command and stays out of the help.
/// </summary>
public static class ExecCommand
{
    public static Command Create(AboxServices services)
    {
        var sandbox = new Argument<string>("sandbox")
        {
            Description = "The sandbox to run the command in.",
        };

        var command = new Command("exec", "Run a command in a sandbox.")
        {
            Arguments = { sandbox },
            Hidden = true,
        };

        command.TreatUnmatchedTokensAsErrors = false;

        command.SetAction((Func<ParseResult, CancellationToken, Task<int>>)(async (parseResult, cancellationToken) =>
        {
            var argv = parseResult.UnmatchedTokens.ToList();
            if (argv.Count == 0)
            {
                services.Console.WriteError("error: no command given. Use: abox exec <sandbox> -- <command>");
                return 1;
            }

            return await CommandHandling.RunAsync(
                async () => await services.Sbx.ExecAsync(
                    new SbxExecRequest(parseResult.GetValue(sandbox)!, argv),
                    cancellationToken),
                services.Console);
        }));

        return command;
    }
}
