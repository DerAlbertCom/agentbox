using System.CommandLine;
using Abox.Configuration;
using Abox.Health;
using Abox.Infrastructure;
using Abox.Kits;
using Abox.Naming;

namespace Abox.Features.Build;

/// <summary>
/// <c>abox build</c>: builds the kit set of a team, every squad of it that no other
/// squad names as its base.
/// </summary>
public sealed class BuildCommand : Command
{
    public BuildCommand(
        Lazy<AboxConfig> config,
        Preflight preflight,
        Lazy<SquadResolver> resolver,
        Lazy<BuildService> build,
        IConsole console
    )
        : base("build", "Build the kit set of a team: its workload and the kits of its squads.")
    {
        var teams = new Option<string[]>("--team")
        {
            Description =
                "The team to build. Can be given more than once. Defaults to the team of the user configuration.",
            AllowMultipleArgumentsPerToken = false,
        };

        var root = new Option<string>("--root")
        {
            Description =
                "The directory to start searching for team sources. Defaults to the current directory.",
        };

        var tag = new Option<string>("--tag")
        {
            Description =
                "The tag the build is stored under. Defaults to the tag of the user configuration, latest.",
        };

        var verify = new Option<bool>("--no-verify")
        {
            Description =
                "Skip additional local kit inspection with sbx. Normal builds still build the workload image.",
        };

        var personal = new Option<bool>("--no-personal")
        {
            Description = "Exclude personal .abox files to make a build safe to publish.",
        };

        var metadata = new Option<bool>("--metadata-only")
        {
            Description =
                "Prepare source metadata without Docker or sbx (maintainers). Replaces the manifest and clears its local image selection; remote create does not need this.",
        };

        Options.Add(teams);
        Options.Add(root);
        Options.Add(tag);
        Options.Add(verify);
        Options.Add(personal);
        Options.Add(metadata);

        SetAction(
            async (parseResult, cancellationToken) =>
            {
                return await CommandHandling.RunAsync(
                    async () =>
                    {
                        var settings = config.Value;
                        var selectedTag = parseResult.GetValue(tag) ?? settings.DefaultTag;
                        RegistryImageName.ValidateTag(selectedTag);
                        var wanted = settings.ResolveTeams(parseResult.GetValue(teams));

                        var rootDirectory = Path.GetFullPath(
                            parseResult.GetValue(root) ?? Directory.GetCurrentDirectory()
                        );
                        var request = new BuildRequest(
                            wanted
                                .Select(team => resolver.Value.ResolveTeam(rootDirectory, team))
                                .ToList(),
                            rootDirectory,
                            selectedTag,
                            !parseResult.GetValue(verify),
                            !parseResult.GetValue(personal),
                            parseResult.GetValue(metadata)
                        );

                        if (
                            !request.MetadataOnly
                            && !await preflight.EnsureReadyAsync(cancellationToken)
                        )
                        {
                            return 1;
                        }

                        return await build.Value.BuildAsync(request, cancellationToken);
                    },
                    console
                );
            }
        );
    }
}
