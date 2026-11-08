using System.CommandLine;
using Abox.Configuration;
using Abox.Health;
using Abox.Infrastructure;
using Abox.Services;

namespace Abox.Cli;

/// <summary>
/// <c>abox build</c>: builds the kit set of a squad and keeps it locally.
/// </summary>
public static class BuildCommand
{
    public static Command Create(
        AboxConfig config,
        Preflight preflight,
        BuildService build,
        IConsole console
    )
    {
        var squad = new Option<string[]>("--squad")
        {
            Description =
                "The squad to build. Can be given more than once. Defaults to the squad of the user configuration.",
            AllowMultipleArgumentsPerToken = false,
        };

        var root = new Option<string>("--root")
        {
            Description =
                "The directory the squad's configurations live in. Defaults to the current directory.",
        };

        var tag = new Option<string>("--tag")
        {
            Description =
                "The tag the build is stored under. Defaults to the tag of the user configuration.",
        };

        var verify = new Option<bool>("--no-verify")
        {
            Description = "Write the kit sources without building and checking them with sbx.",
        };

        var command = new Command(
            "build",
            "Build the kit set of a squad: its workload and its kits."
        )
        {
            Options = { squad, root, tag, verify },
        };

        command.SetAction(
            async (parseResult, cancellationToken) =>
            {
                var squads = parseResult.GetValue(squad);
                var request = new BuildRequest(
                    squads is { Length: > 0 } ? squads : [config.DefaultSquad],
                    parseResult.GetValue(root) ?? Directory.GetCurrentDirectory(),
                    parseResult.GetValue(tag) ?? config.DefaultTag,
                    !parseResult.GetValue(verify)
                );

                return await CommandHandling.RunAsync(
                    async () =>
                    {
                        if (!await preflight.EnsureReadyAsync(cancellationToken))
                        {
                            return 1;
                        }

                        return await build.BuildAsync(request, cancellationToken);
                    },
                    console
                );
            }
        );

        return command;
    }
}
