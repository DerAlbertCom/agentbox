using Abox.Infrastructure;
using Abox.Kits;
using Abox.LocalState;
using Abox.Naming;

namespace Abox.Features.Publish;

internal sealed record SelectedPublicationBuild(string Team, BuildManifest Build);

/// <summary>Validates the entire selection, then prepares private fingerprint-checked sources.</summary>
internal sealed class PublicationSources(BuildStore store)
{
    internal IReadOnlyList<SelectedPublicationBuild> LoadBuilds(PublishRequest request)
    {
        RejectCaseCollisions(request);
        var builds = request
            .Teams.SelectMany(team =>
                team.Squads.Select(squad => new SelectedPublicationBuild(
                    team.Name,
                    LoadBuild(team.Name, squad, request.Tag)
                ))
            )
            .ToList();
        if (builds.Count == 0)
            throw new AboxException("No squad builds were selected for publication.");
        return builds;
    }

    internal static IReadOnlyList<IReadOnlyDictionary<string, string>> PrepareSnapshots(
        IReadOnlyList<SelectedPublicationBuild> builds,
        string directory
    )
    {
        // All teams' sources and supplied local arguments are checked before any registry write.
        var snapshots = builds
            .Select(
                (item, index) =>
                    BuildSources.Snapshot(
                        item.Build,
                        Path.Combine(directory, "sources", index.ToString())
                    )
            )
            .ToList();
        for (var index = 0; index < builds.Count; index++)
        {
            foreach (var kit in builds[index].Build.Kits.Where(kit => kit.IsLocal))
                ValidateLocalArguments(snapshots[index][kit.Reference], kit.Arguments);
        }
        return snapshots;
    }

    private BuildManifest LoadBuild(string team, string squad, string tag)
    {
        RegistryImageName.SquadRepository(team, squad);
        var build =
            store.Load(team, squad, tag)
            ?? throw new AboxException(
                $"No local build of '{squad}:{tag}'. Run 'abox build --team {team} --tag {tag} --no-personal' first."
            );
        if (build.IncludesPersonalConfigurations != false)
            throw new AboxException(
                "This build includes personal configuration or has unknown configuration provenance. Run 'abox build --no-personal' for the team and tag before publishing."
            );
        if (!WorkloadFile.IsValidPublishRepository(build.PublishRepository))
            throw new AboxException(
                $"The local build '{squad}:{tag}' has no valid publishRepository. Set 'publishRepository' in its .workload file and run 'abox build --team {team} --tag {tag} --no-personal' again before publishing."
            );
        Descriptor(build.WorkloadKitDirectory);
        foreach (var kit in build.Kits.Where(kit => kit.IsLocal))
            Descriptor(kit.Reference);
        if (build.StateMixinDirectory is { } state)
            Descriptor(state);
        BuildSources.EnsureUnchanged(build);
        return build;
    }

    private static void ValidateLocalArguments(
        string directory,
        IReadOnlyDictionary<string, string> arguments
    )
    {
        var declarations = YamlReader.LoadFile(Descriptor(directory)).GetEntry("args");
        foreach (var name in arguments.Keys)
        {
            var declaration = declarations?.GetEntry(name);
            if (
                declaration is null
                || declaration.GetString("buildArg") is not null
                || declaration.GetString("buildSecret") is not null
            )
                throw new AboxException(
                    $"Kit argument '{name}' is undeclared or build-phase. Publishing supplied build-phase arguments is not supported; only create-phase arguments may be fixed in a set."
                );
        }
    }

    private static void RejectCaseCollisions(PublishRequest request)
    {
        if (
            request
                .Teams.Select(team => team.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() != request.Teams.Count
            || request.Teams.Any(team =>
                team.Squads.Distinct(StringComparer.OrdinalIgnoreCase).Count() != team.Squads.Count
            )
        )
            throw new AboxException(
                "Selected team or squad names collide when compared without case. Give every team and squad a distinct registry identity."
            );
        foreach (var team in request.Teams)
        {
            var parent = Path.GetDirectoryName(Path.GetFullPath(team.Folder));
            var duplicateFolder =
                parent is not null
                && Directory.Exists(parent)
                && Directory
                    .EnumerateDirectories(parent)
                    .Count(folder =>
                        Path.GetFileName(folder)
                            .Equals(team.Name, StringComparison.OrdinalIgnoreCase)
                    ) > 1;
            var duplicateSquad =
                Directory.Exists(team.Folder)
                && Directory
                    .EnumerateFiles(team.Folder, "*.abox")
                    .GroupBy(Path.GetFileNameWithoutExtension, StringComparer.OrdinalIgnoreCase)
                    .Any(group => group.Count() > 1);
            if (duplicateFolder || duplicateSquad)
                throw new AboxException(
                    "Team folders or squad files have names that collide without case; rename them before publishing."
                );
        }
    }

    internal static string Descriptor(string directory) =>
        KitDescriptor.Load(directory)?.DescriptorPath
        ?? throw new AboxException(
            $"The built kit at '{directory}' has no descriptor. Run 'abox build' again."
        );
}
