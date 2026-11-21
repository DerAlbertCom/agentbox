using System.CommandLine;
using Abox.Cli;
using Abox.Infrastructure;
using Abox.Sbx;

namespace Abox.Features.Sandbox;

/// <summary>Common construction for the hidden commands that exec into a named sandbox.</summary>
internal static class SandboxCommand
{
    internal static void Configure(
        Command command,
        ISbxClient sbx,
        TrailingArguments trailing,
        IConsole console,
        bool keepStandardInputOpen = false
    )
    {
        command.Hidden = true;
        var sandbox = new Argument<string>("sandbox")
        {
            Description = "The sandbox to run the command in.",
        };
        command.Arguments.Add(sandbox);
        command.SetAction(
            async (parseResult, cancellationToken) =>
            {
                var argv = trailing.Values;
                if (argv.Count == 0)
                {
                    console.WriteError(
                        $"error: no command given. Use: abox {command.Name} <sandbox> -- <command>"
                    );
                    return 1;
                }

                return await CommandHandling.RunAsync(
                    () =>
                        sbx.ExecAsync(
                            new SbxExecRequest(
                                parseResult.GetValue(sandbox)!,
                                argv,
                                KeepStandardInputOpen: keepStandardInputOpen
                            ),
                            cancellationToken
                        ),
                    console
                );
            }
        );
    }
}
