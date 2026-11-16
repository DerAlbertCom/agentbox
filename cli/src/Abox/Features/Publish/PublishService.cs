using System.Text.Json;
using Abox.Infrastructure;
using Abox.Kits;
using Abox.LocalState;
using Abox.Naming;

namespace Abox.Features.Publish;

/// <summary>All teams to validate before publishing, and the optional Buildx builder.</summary>
public sealed record PublishRequest(
    IReadOnlyList<SquadResolver.ResolvedTeam> Teams,
    string Tag,
    string? Builder = null
);

/// <summary>Publishes verified source snapshots as v3 kits, then promotes a verified set.</summary>
public sealed class PublishService(
    BuildStore store,
    IHostPrograms programs,
    IOciRegistry registry,
    IConsole console
)
{
    public const string Registry = RegistryImageName.Registry;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public async Task<int> PublishAsync(PublishRequest request, CancellationToken cancellationToken)
    {
        RegistryImageName.ValidateTag(request.Tag);
        var builds = new PublicationSources(store).LoadBuilds(request);
        OciRegistry.RequireDocker(
            programs,
            "Publishing v3 kits needs Docker with the Buildx plugin on PATH. Install it and configure a BuildKit builder; see docs/implementation.md."
        );

        using var temporary = new TemporaryDirectory("abox-publish-", "publication", console);
        try
        {
            var snapshots = PublicationSources.PrepareSnapshots(builds, temporary.Path);
            var publisher = new VerifiedPublicationRegistry(registry, console, request.Builder);
            await publisher.RequireBuildxAsync(cancellationToken);
            await publisher.ValidateRegistryKitsAsync(
                builds.SelectMany(item => item.Build.Kits),
                cancellationToken
            );
            for (var index = 0; index < builds.Count; index++)
            {
                var selected = builds[index];
                var publication = new Publication(
                    Path.Combine(temporary.Path, "publication", index.ToString()),
                    $"{Registry}/{selected.Build.PublishRepository}/{RegistryImageName.SquadRepository(selected.Team, selected.Build.Squad)}",
                    "publish-" + Guid.NewGuid().ToString("N"),
                    publisher
                );
                await PublishSquadAsync(
                    publication,
                    selected,
                    snapshots[index],
                    request.Tag,
                    cancellationToken
                );
            }
            return 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new AboxException(
                $"Cannot prepare or read publication files: {exception.Message}",
                exception
            );
        }
    }

    private async Task PublishSquadAsync(
        Publication publication,
        SelectedPublicationBuild selected,
        IReadOnlyDictionary<string, string> snapshots,
        string tag,
        CancellationToken cancellationToken
    )
    {
        Directory.CreateDirectory(publication.Directory);
        var kits = await PublishVerifiedComponentsAsync(
            publication,
            selected.Build,
            snapshots,
            cancellationToken
        );
        var configDirectory = WriteConfiguration(
            publication.Directory,
            selected.Team,
            selected.Build
        );
        var config = await publication.Publisher.PushVerifiedAsync(
            publication.Component(configDirectory, "config", PublicationImageKind.Mixin),
            cancellationToken
        );
        kits.Add(new PublicationComponent(config));
        var candidate = await PublishVerifiedCandidateAsync(
            publication,
            selected,
            kits,
            cancellationToken
        );
        var reference = $"{publication.Repository}:{tag}";
        // The requested (possibly moving) tag is untouched until the candidate passes inspection.
        await publication.Publisher.PromoteAndConfirmAsync(candidate, reference, cancellationToken);
        console.WriteLine($"Published squad '{selected.Build.Squad}' as '{reference}'.");
    }

    private sealed record Publication(
        string Directory,
        string Repository,
        string Id,
        VerifiedPublicationRegistry Publisher
    )
    {
        internal PublicationPush Component(
            string directory,
            string name,
            PublicationImageKind kind
        ) => new(directory, $"{Repository}/components/{name}:{Id}", kind, Directory);
    }

    private static async Task<List<PublicationComponent>> PublishVerifiedComponentsAsync(
        Publication publication,
        BuildManifest build,
        IReadOnlyDictionary<string, string> snapshots,
        CancellationToken cancellationToken
    )
    {
        var kits = new List<PublicationComponent>();
        var workload = await publication.Publisher.PushVerifiedAsync(
            publication.Component(
                snapshots[build.WorkloadKitDirectory],
                "workload",
                PublicationImageKind.Workload
            ),
            cancellationToken
        );
        kits.Add(new PublicationComponent(workload));
        for (var index = 0; index < build.Kits.Count; index++)
        {
            var kit = build.Kits[index];
            var source = kit.IsLocal
                ? await publication.Publisher.PushVerifiedAsync(
                    publication.Component(
                        snapshots[kit.Reference],
                        $"kit-{index}",
                        PublicationImageKind.Mixin
                    ),
                    cancellationToken
                )
                : kit.Reference;
            kits.Add(new PublicationComponent(source, kit.Arguments));
        }
        if (build.StateMixinDirectory is { } state)
        {
            var source = await publication.Publisher.PushVerifiedAsync(
                publication.Component(snapshots[state], "state", PublicationImageKind.Mixin),
                cancellationToken
            );
            kits.Add(new PublicationComponent(source));
        }
        return kits;
    }

    private static async Task<string> PublishVerifiedCandidateAsync(
        Publication publication,
        SelectedPublicationBuild selected,
        IReadOnlyList<PublicationComponent> kits,
        CancellationToken cancellationToken
    )
    {
        var directory = Path.Combine(publication.Directory, "set");
        Directory.CreateDirectory(directory);
        WriteDescriptor(
            Path.Combine(directory, "set.yaml"),
            new PublicationSetDescriptor
            {
                DisplayName = $"{selected.Team} / {selected.Build.Squad}",
                Kits = kits,
            }
        );
        return await publication.Publisher.PushVerifiedAsync(
            new PublicationPush(
                directory,
                $"{publication.Repository}:{publication.Id}",
                PublicationImageKind.Set,
                publication.Directory
            ),
            cancellationToken
        );
    }

    private static string WriteConfiguration(string directory, string team, BuildManifest build)
    {
        var configDirectory = Path.Combine(directory, "config");
        Directory.CreateDirectory(configDirectory);
        File.WriteAllText(
            Path.Combine(configDirectory, "squad.json"),
            JsonSerializer.Serialize(
                new PublicationSquadMetadata
                {
                    Team = team,
                    Squad = build.Squad,
                    Tag = build.Tag,
                    CreatedAt = build.CreatedAt,
                    Mcp = build.Mcp.Select(mcp => new PublicationMcp(mcp.Name, mcp.Url)).ToList(),
                    State = build
                        .State.Select(state => new PublicationState(
                            state.SandboxPath,
                            state.RelativePath
                        ))
                        .ToList(),
                    Kits = build
                        .Kits.Select(kit => new PublicationKitMetadata(kit.Name, kit.Arguments))
                        .ToList(),
                },
                JsonOptions
            )
        );
        WriteDescriptor(
            Path.Combine(configDirectory, "config.yaml"),
            new PublicationConfigurationDescriptor()
        );
        return configDirectory;
    }

    private static void WriteDescriptor<TDescriptor>(string path, TDescriptor descriptor) =>
        File.WriteAllText(
            path,
            "# syntax=docker/sandbox-kit:3\n" + JsonSerializer.Serialize(descriptor, JsonOptions)
        );
}
