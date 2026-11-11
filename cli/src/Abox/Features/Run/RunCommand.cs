using System.CommandLine;
using Abox.Cli;
using Abox.Configuration;
using Abox.Health;
using Abox.Infrastructure;
using Abox.Launchers;

namespace Abox.Features.Run;

/// <summary>
/// <c>abox run</c>: starts a launcher for the sandbox of a folder.
/// </summary>
public sealed class RunCommand : Command
{
    public RunCommand(
        Lazy<AboxConfig> config,
        LauncherRegistry launchers,
        Preflight preflight,
        Lazy<RunService> run,
        TrailingArguments trailing,
        IConsole console
    )
        : base("run", "Start a launcher for the sandbox of a folder.")
    {
        var launcher = new Argument<string>("launcher")
        {
            Arity = ArgumentArity.ZeroOrOne,
            Description = "The launcher to start. Defaults to the shell.",
        };

        launcher.CompletionSources.Add(launchers.All.Select(item => item.Id).ToArray());

        var location = new Option<string>("--location")
        {
            Description =
                "The folder whose sandbox is opened. Defaults to the location of the user configuration, or the current directory.",
        };

        Arguments.Add(launcher);
        Options.Add(location);

        Description =
            "Start a launcher for the sandbox of a folder."
            + Environment.NewLine
            + Environment.NewLine
            + "Launchers:"
            + Environment.NewLine
            + string.Join(
                Environment.NewLine,
                launchers.All.Select(item => $"  {item.Id, -10} {item.Summary}")
            );

        SetAction(
            async (parseResult, cancellationToken) =>
            {
                return await CommandHandling.RunAsync(
                    async () =>
                    {
                        var request = new RunRequest(
                            parseResult.GetValue(launcher) ?? "shell",
                            config.Value.ResolveLocation(parseResult.GetValue(location)),
                            trailing.Values
                        );

                        if (!await preflight.EnsureReadyAsync(cancellationToken))
                        {
                            return 1;
                        }

                        return await run.Value.RunAsync(request, cancellationToken);
                    },
                    console
                );
            }
        );
    }
}
