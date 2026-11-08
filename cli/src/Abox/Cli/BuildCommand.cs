using System.CommandLine;
using Abox.Services;

namespace Abox.Cli;

/// <summary>
/// <c>abox build</c>: builds the kit set of a squad and keeps it locally.
/// </summary>
public static class BuildCommand
{
    public static Command Create(AboxServices services)
    {
        var squad = new Option<string>("--squad")
        {
            Description = "The squad to build. Defaults to the squad of the user configuration.",
        };

        var root = new Option<string>("--root")
        {
            Description = "The directory the squad's configurations live in. Defaults to the current directory.",
        };

        var tag = new Option<string>("--tag")
        {
            Description = "The tag the build is stored under. Defaults to the tag of the user configuration.",
        };

        var verify = new Option<bool>("--no-verify")
        {
            Description = "Write the kit sources without building and checking them with sbx.",
        };

        var command = new Command("build", "Build the kit set of a squad: its workload and its kits.")
        {
            Options = { squad, root, tag, verify },
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var request = new BuildRequest(
                parseResult.GetValue(squad) ?? services.Config.DefaultSquad,
                parseResult.GetValue(root) ?? Directory.GetCurrentDirectory(),
                parseResult.GetValue(tag) ?? services.Config.DefaultTag,
                !parseResult.GetValue(verify));

            return await CommandHandling.RunAsync(
                async () =>
                {
                    if (!await services.Preflight.EnsureReadyAsync(cancellationToken))
                    {
                        return 1;
                    }

                    return await services.Build.BuildAsync(request, cancellationToken);
                },
                services.Console);
        });

        return command;
    }
}
