using Abox.Build;
using Abox.Infrastructure;
using Abox.Kits;
using Abox.Sbx;

namespace Abox.Features.Build;

/// <summary>What to build.</summary>
/// <param name="Teams">The teams to build, each with the squads a team build makes.</param>
/// <param name="RootDirectory">The directory of the project that holds its teams folder.</param>
/// <param name="Tag">The tag the builds are stored under.</param>
/// <param name="Verify">Whether additional local kit declarations are checked with sbx.</param>
/// <param name="IncludePersonalConfigurations">Whether personal .abox files are merged into this local build.</param>
/// <param name="MetadataOnly">Prepare only source/configuration metadata, without invoking external tools or selecting a local image.</param>
public sealed record BuildRequest(
    IReadOnlyList<SquadResolver.ResolvedTeam> Teams,
    string RootDirectory,
    string Tag,
    bool Verify = true,
    bool IncludePersonalConfigurations = true,
    bool MetadataOnly = false
);

/// <summary>
/// Builds the squads of the teams: resolves each squad, writes the workload kit and
/// the state mixin, prebuilds the workload image and keeps the manifest of the result.
/// </summary>
public sealed class BuildService(
    SquadResolver resolver,
    WorkloadKitGenerator workloadGenerator,
    StateMixinGenerator stateGenerator,
    PersonalConfigurations personalConfigurations,
    BuildStore store,
    LocalWorkloadImages images,
    ISbxClient sbx,
    IConsole console
)
{
    /// <summary>Builds the squads of the teams and returns the exit code of the command.</summary>
    public async Task<int> BuildAsync(BuildRequest request, CancellationToken cancellationToken)
    {
        foreach (var team in request.Teams)
        {
            foreach (var squad in team.Squads)
            {
                await BuildSquadAsync(team.Name, squad, request, cancellationToken);
            }
        }

        return 0;
    }

    private async Task BuildSquadAsync(
        string teamName,
        string squadName,
        BuildRequest request,
        CancellationToken cancellationToken
    )
    {
        var personalFiles = request.IncludePersonalConfigurations
            ? personalConfigurations.Files
            : [];
        var squad = resolver.Resolve(request.RootDirectory, teamName, squadName, personalFiles);
        var buildDirectory = store.DirectoryFor(squad.Name, request.Tag);

        var workloadDirectory = Path.Combine(buildDirectory, WorkloadKitGenerator.KitName);
        workloadGenerator.Generate(squad.Workload, workloadDirectory);
        console.WriteLine(
            $"Workload kit '{squad.Workload.Name}' written to '{workloadDirectory}'."
        );

        string? stateDirectory = null;
        if (squad.State.Count > 0)
        {
            stateDirectory = Path.Combine(buildDirectory, StateMixinGenerator.MixinName);
            stateGenerator.Generate(squad.State, stateDirectory);
        }

        var manifest = Manifest(teamName, squad, request, workloadDirectory, stateDirectory);
        manifest.IncludesPersonalConfigurations = personalFiles.Count > 0;
        if (!request.IncludePersonalConfigurations)
        {
            manifest.SourceHashes = BuildSources.Capture(manifest);
        }
        if (!request.MetadataOnly)
        {
            if (request.Verify)
            {
                await VerifyAsync(squad, stateDirectory, cancellationToken);
            }

            manifest.LocalWorkload = await images.BuildAsync(
                workloadDirectory,
                Path.Combine(
                    buildDirectory,
                    "runtime",
                    Guid.NewGuid().ToString("N"),
                    WorkloadKitGenerator.KitName
                ),
                cancellationToken
            );
        }

        if (!request.IncludePersonalConfigurations)
        {
            BuildSources.EnsureUnchanged(manifest);
        }
        store.Save(manifest);

        if (request.MetadataOnly)
        {
            console.WriteLine(
                $"Prepared metadata only for squad '{squad.Name}:{request.Tag}'. No image was built or selected."
            );
            console.WriteLine(
                "Any previous local image selection for this squad/tag is invalidated; existing image/runtime artifacts are not deleted. Run a build without --metadata-only before using --local."
            );
        }
        else
        {
            console.WriteLine($"Squad '{squad.Name}' built as '{request.Tag}'.");
        }
        console.WriteLine(
            $"Create a sandbox from {(request.MetadataOnly ? "the matching published set" : "it")} with: abox create --team {teamName} --squad {squad.Name} --tag {request.Tag}{(request.MetadataOnly ? "" : " --local")}"
        );
    }

    private async Task VerifyAsync(
        ResolvedSquad squad,
        string? stateDirectory,
        CancellationToken cancellationToken
    )
    {
        var kits =
            new List<(
                string Name,
                string Directory,
                IReadOnlyDictionary<string, string> Arguments
            )>();

        kits.AddRange(
            squad
                .Kits.Where(kit => kit.IsLocal)
                .Select(kit => (kit.Name, kit.Directory!, kit.Arguments))
        );

        if (stateDirectory is not null)
        {
            kits.Add(
                (StateMixinGenerator.MixinName, stateDirectory, new Dictionary<string, string>())
            );
        }

        foreach (var (name, directory, arguments) in kits)
        {
            console.WriteLine($"Checking kit declarations '{name}'...");
            var outcome = await sbx.InspectKitAsync(directory, cancellationToken, arguments);
            if (!outcome.Succeeded)
            {
                throw new AboxException(
                    $"The kit '{name}' could not be inspected (use --no-verify for recipe-based mixins; sbx inspect is declaration-only):{Environment.NewLine}{outcome.Output.Trim()}"
                );
            }
        }
    }

    private static BuildManifest Manifest(
        string teamName,
        ResolvedSquad squad,
        BuildRequest request,
        string workloadDirectory,
        string? stateDirectory
    )
    {
        var manifest = new BuildManifest
        {
            Squad = squad.Name,
            Tag = request.Tag,
            Reference = ImageName.OfTeam(teamName, request.Tag),
            CreatedAt = DateTimeOffset.Now,
            RootDirectory = squad.RootDirectory,
            WorkloadName = squad.Workload.Name,
            PublishRepository = squad.Workload.PublishRepository,
            WorkloadKitDirectory = workloadDirectory,
            Template = squad.Workload.Template,
            ConfigFiles = [.. squad.ConfigFiles, squad.Workload.Path],
            StateMixinDirectory = stateDirectory,
        };

        foreach (var kit in squad.Kits)
        {
            manifest.Kits.Add(
                new ManifestKit
                {
                    Name = kit.Name,
                    Reference = kit.Reference,
                    IsLocal = kit.IsLocal,
                    Arguments = kit.Arguments.ToDictionary(
                        argument => argument.Key,
                        argument => argument.Value
                    ),
                }
            );
        }

        foreach (var server in squad.Mcp)
        {
            manifest.Mcp.Add(new ManifestMcp { Name = server.Name, Url = server.Url });
        }

        foreach (var state in squad.State)
        {
            manifest.State.Add(
                new ManifestState
                {
                    SandboxPath = state.SandboxPath,
                    RelativePath = state.RelativePath,
                }
            );
        }

        return manifest;
    }
}
