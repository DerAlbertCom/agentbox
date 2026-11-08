using System.CommandLine;
using Abox.Health;
using Abox.Infrastructure;
using Abox.Launchers;
using Abox.Services;

namespace Abox.Cli;

/// <summary>
/// <c>abox run</c>: starts a launcher for the sandbox of a folder.
/// </summary>
public static class RunCommand
{
    public static Command Create(
        LauncherRegistry launchers,
        Preflight preflight,
        RunService run,
        TrailingArguments trailing,
        IConsole console
    )
    {
        var launcher = new Argument<string>("launcher")
        {
            Arity = ArgumentArity.ZeroOrOne,
            Description = "The launcher to start. Defaults to the shell.",
        };

        launcher.CompletionSources.Add(launchers.All.Select(item => item.Id).ToArray());

        var location = new Option<string>("--location")
        {
            Description = "The folder whose sandbox is opened. Defaults to the current directory.",
        };

        var command = new Command("run", "Start a launcher for the sandbox of a folder.")
        {
            Arguments = { launcher },
            Options = { location },
        };

        command.Description =
            "Start a launcher for the sandbox of a folder."
            + Environment.NewLine
            + Environment.NewLine
            + "Launchers:"
            + Environment.NewLine
            + string.Join(
                Environment.NewLine,
                launchers.All.Select(item => $"  {item.Id, -10} {item.Summary}")
            );

        command.SetAction(
            async (parseResult, cancellationToken) =>
            {
                var request = new RunRequest(
                    parseResult.GetValue(launcher) ?? "shell",
                    parseResult.GetValue(location) ?? Directory.GetCurrentDirectory(),
                    trailing.Values
                );

                return await CommandHandling.RunAsync(
                    async () =>
                    {
                        if (!await preflight.EnsureReadyAsync(cancellationToken))
                        {
                            return 1;
                        }

                        return await run.RunAsync(request, cancellationToken);
                    },
                    console
                );
            }
        );

        return command;
    }
}
