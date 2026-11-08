using System.CommandLine;
using Abox.Services;

namespace Abox.Cli;

/// <summary>
/// <c>abox run</c>: starts a launcher for the sandbox of a folder.
/// </summary>
public static class RunCommand
{
    public static Command Create(AboxServices services)
    {
        var launcher = new Argument<string>("launcher")
        {
            Arity = ArgumentArity.ZeroOrOne,
            Description = "The launcher to start. Defaults to the shell.",
        };

        launcher.CompletionSources.Add(services.Launchers.All.Select(item => item.Id).ToArray());

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
                services.Launchers.All.Select(item => $"  {item.Id, -10} {item.Summary}")
            );

        command.SetAction(
            async (parseResult, cancellationToken) =>
            {
                var request = new RunRequest(
                    parseResult.GetValue(launcher) ?? "shell",
                    parseResult.GetValue(location) ?? Directory.GetCurrentDirectory(),
                    services.Trailing.Values
                );

                return await CommandHandling.RunAsync(
                    async () =>
                    {
                        if (!await services.Preflight.EnsureReadyAsync(cancellationToken))
                        {
                            return 1;
                        }

                        return await services.Run.RunAsync(request, cancellationToken);
                    },
                    services.Console
                );
            }
        );

        return command;
    }
}
