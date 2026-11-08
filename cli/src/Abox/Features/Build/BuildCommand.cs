using System.CommandLine;
using Abox.Configuration;
using Abox.Health;
using Abox.Infrastructure;

namespace Abox.Features.Build;

/// <summary>
/// <c>abox build</c>: builds the kit set of a squad and keeps it locally.
/// </summary>
public sealed class BuildCommand : Command
{
    public BuildCommand(
        AboxConfig config,
        Preflight preflight,
        BuildService build,
        IConsole console
    )
        : base("build", "Build the kit set of a squad: its workload and its kits.")
    {
        var squads = new Option<string[]>("--squad")
        {
            Description =
                "The squad to build. Can be given more than once. Defaults to the squad of the user configuration.",
            AllowMultipleArgumentsPerToken = false,
        };

        var root = new Option<string>("--root")
        {
            Description =
                "The directory of the project that holds the teams folder. Defaults to the current directory.",
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

        Options.Add(squads);
        Options.Add(root);
        Options.Add(tag);
        Options.Add(verify);

        SetAction(
            async (parseResult, cancellationToken) =>
            {
                var wanted = parseResult.GetValue(squads);
                var request = new BuildRequest(
                    wanted is { Length: > 0 } ? wanted : [config.DefaultSquad],
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
    }
}
