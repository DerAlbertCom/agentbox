using Abox.Configuration;
using Abox.Infrastructure;
using Abox.Kits;
using Abox.LocalState;
using Abox.Naming;

namespace Abox.Sbx;

/// <summary>A squad identity resolved before validating the sandbox location.</summary>
public sealed record SquadSelection(
    string Team,
    string Squad,
    string Tag,
    string? Reference = null
);

/// <summary>The source-specific inputs to the shared sandbox creation steps.</summary>
public sealed record PreparedSquad(
    BuildManifest Metadata,
    string KitSource,
    IReadOnlyList<string> Mixins,
    IReadOnlyDictionary<string, string> Arguments,
    string PullPolicy,
    string? DockerContext = null,
    string? KitReference = null
);

/// <summary>Resolves a squad identity, then prepares its kits after location validation.</summary>
public interface ISquadSource
{
    SquadSelection Resolve(SandboxCreationRequest request);
    Task<PreparedSquad> LoadAsync(SquadSelection selection, CancellationToken cancellationToken);
}

/// <summary>Uses only validated local build metadata and prebuilt workload images.</summary>
public sealed class LocalSquadSource(BuildStore store, LocalWorkloadImages images, IConsole console)
    : ISquadSource
{
    public SquadSelection Resolve(SandboxCreationRequest request)
    {
        var squad = request.Squad ?? PickSquad(request.Team);
        squad =
            request.Team.Squads.FirstOrDefault(name =>
                name.Equals(squad, StringComparison.OrdinalIgnoreCase)
            )
            ?? throw new AboxException(
                $"There is no squad '{squad}' in team '{request.Team.Name}'."
            );
        return new SquadSelection(request.Team.Name, squad, request.Tag);
    }

    public async Task<PreparedSquad> LoadAsync(
        SquadSelection selection,
        CancellationToken cancellationToken
    )
    {
        var manifest =
            store.Load(selection.Squad, selection.Tag)
            ?? throw new AboxException(
                $"There is no local build of '{selection.Squad}' with the tag '{selection.Tag}'. "
                    + $"Prepare it first: abox build --team {selection.Team} --tag {selection.Tag}"
            );
        if (
            manifest.Reference != ImageName.OfTeam(selection.Team, selection.Tag)
            || manifest.Squad != selection.Squad
            || manifest.Tag != selection.Tag
        )
        {
            throw new AboxException(
                $"The local build metadata of '{selection.Squad}' is not for team '{selection.Team}'. "
                    + $"Prepare it again: abox build --team {selection.Team} --tag {selection.Tag}"
            );
        }
        var image =
            manifest.LocalWorkload
            ?? throw new AboxException(
                $"This manifest has no prebuilt local workload (metadata-only or legacy build). Run 'abox build --team {selection.Team} --tag {selection.Tag}' without --metadata-only before using --local. Create will not build the workload from sources."
            );
        await images.EnsureAvailableAsync(image, cancellationToken);
        var arguments = KitArguments(manifest.Kits);
        var mixins = manifest.Kits.Select(kit => kit.Reference).ToList();
        if (manifest.StateMixinDirectory is not null)
            mixins.Add(manifest.StateMixinDirectory);
        return new PreparedSquad(
            manifest,
            image.RuntimeKitDirectory,
            mixins,
            arguments,
            PullPolicy: "never",
            DockerContext: image.DockerContext
        );
    }

    private string PickSquad(SquadResolver.ResolvedTeam team)
    {
        if (team.Squads.Count == 1)
            return team.Squads[0];
        var pick = console.Choose(
            $"Which squad of the team '{team.Name}' shall the sandbox be made of?",
            team.Squads
        );
        return pick is { } index
            ? team.Squads[index]
            : throw new AboxException("No squad is given and none was picked.");
    }

    private static Dictionary<string, string> KitArguments(IReadOnlyList<ManifestKit> kits)
    {
        var arguments = new Dictionary<string, string>(StringComparer.Ordinal);
        var owners = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var kit in kits)
        {
            foreach (var (key, value) in kit.Arguments)
            {
                if (arguments.TryGetValue(key, out var existing) && existing != value)
                    throw new AboxException(
                        $"The kit argument '{key}' has conflicting values in kits '{owners[key]}' and '{kit.Name}'."
                    );
                if (arguments.TryAdd(key, value))
                    owners.Add(key, kit.Name);
            }
        }
        return arguments;
    }
}

/// <summary>Uses a published squad set, with no local build metadata fallback.</summary>
public sealed class RemoteSquadSource(
    AboxConfig config,
    ISbxClient sbx,
    IPublishedSquadSource published
) : ISquadSource
{
    public SquadSelection Resolve(SandboxCreationRequest request)
    {
        var squad =
            request.Squad
            ?? throw new AboxException(
                "Remote create requires --squad <name>; published squad discovery is not supported."
            );
        var reference = RegistryImageName.OfSquad(
            config.PullRepository,
            request.Team.Name,
            squad,
            request.Tag
        );
        return new SquadSelection(request.Team.Name, squad, request.Tag, reference);
    }

    public async Task<PreparedSquad> LoadAsync(
        SquadSelection selection,
        CancellationToken cancellationToken
    )
    {
        await sbx.EnsureKitSourceAllowedAsync(
            RegistryImageName.PullRepositoryPrefix(config.PullRepository),
            cancellationToken
        );
        var remote = await published.LoadAsync(
            selection.Reference!,
            selection.Team,
            selection.Squad,
            selection.Tag,
            cancellationToken
        );
        return new PreparedSquad(
            remote.Metadata,
            remote.Reference,
            [],
            new Dictionary<string, string>(StringComparer.Ordinal),
            PullPolicy: "always",
            KitReference: remote.Reference
        );
    }
}
