using System.Text.Json;
using System.Text.RegularExpressions;
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
        if (!Regex.IsMatch(request.Tag, @"\A[A-Za-z0-9_][A-Za-z0-9_.-]{0,127}\z"))
        {
            throw new AboxException($"'{request.Tag}' is not a valid registry tag.");
        }

        RejectCaseCollisions(request);
        var builds = request
            .Teams.SelectMany(team =>
                team.Squads.Select(squad =>
                    (Team: team, Build: LoadBuild(team, squad, request.Tag))
                )
            )
            .ToList();
        if (builds.Count == 0)
        {
            throw new AboxException("No squad builds were selected for publication.");
        }

        if (!programs.Exists("docker"))
        {
            throw new AboxException(
                "Publishing v3 kits needs Docker with the Buildx plugin on PATH. Install it and configure a BuildKit builder; see docs/implementation.md."
            );
        }

        using var temporary = new TemporaryDirectory("abox-publish-", "publication", console);
        var directory = temporary.Path;
        try
        {
            var snapshots = SnapshotBuilds(builds.Select(item => item.Build).ToList(), directory);

            await RunAsync(
                ["buildx", "version"],
                attachConsole: false,
                cancellationToken: cancellationToken
            );
            await ValidateRegistryKitsAsync(
                builds.SelectMany(item => item.Build.Kits),
                request.Builder,
                cancellationToken
            );

            for (var index = 0; index < builds.Count; index++)
            {
                await PublishSquadAsync(
                    request,
                    builds[index].Team,
                    builds[index].Build,
                    snapshots[index],
                    Path.Combine(directory, "publication", index.ToString()),
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

    private static IReadOnlyList<IReadOnlyDictionary<string, string>> SnapshotBuilds(
        IReadOnlyList<BuildManifest> builds,
        string directory
    )
    {
        // Snapshot every team's validated inputs before the first registry write.
        var snapshots = builds
            .Select(
                (build, index) =>
                    BuildSources.Snapshot(
                        build,
                        Path.Combine(directory, "sources", index.ToString())
                    )
            )
            .ToList();
        for (var index = 0; index < builds.Count; index++)
        {
            foreach (var kit in builds[index].Kits.Where(kit => kit.IsLocal))
                ValidateLocalArguments(snapshots[index][kit.Reference], kit.Arguments);
        }
        return snapshots;
    }

    private async Task ValidateRegistryKitsAsync(
        IEnumerable<ManifestKit> kits,
        string? builder,
        CancellationToken cancellationToken
    )
    {
        foreach (var kit in kits.Where(kit => !kit.IsLocal))
        {
            var descriptors = await InspectAsync(
                kit.Reference,
                "mixin",
                cancellationToken,
                builder
            );
            foreach (var descriptor in descriptors)
                ValidateRegistryArguments(descriptor, kit.Arguments);
        }
    }

    private BuildManifest LoadBuild(SquadResolver.ResolvedTeam team, string squad, string tag)
    {
        RepositoryName(team.Name, squad);
        var build =
            store.Load(squad, tag)
            ?? throw new AboxException(
                $"No local build of '{squad}:{tag}'. Run 'abox build --team {team.Name} --tag {tag} --no-personal' first."
            );
        if (
            build.Squad != squad
            || build.Tag != tag
            || build.Reference != ImageName.OfTeam(team.Name, tag)
        )
        {
            throw new AboxException(
                $"The local build '{squad}:{tag}' is not a build of team '{team.Name}'. Run 'abox build --team {team.Name} --tag {tag} --no-personal' again."
            );
        }

        if (build.IncludesPersonalConfigurations != false)
        {
            throw new AboxException(
                "This build includes personal configuration or has unknown configuration provenance. Run 'abox build --no-personal' for the team and tag before publishing."
            );
        }

        if (!WorkloadFile.IsValidPublishRepository(build.PublishRepository))
        {
            throw new AboxException(
                $"The local build '{squad}:{tag}' has no valid publishRepository. Set 'publishRepository' in its .workload file and run 'abox build --team {team.Name} --tag {tag} --no-personal' again before publishing."
            );
        }

        Descriptor(build.WorkloadKitDirectory);
        foreach (var kit in build.Kits.Where(kit => kit.IsLocal))
        {
            Descriptor(kit.Reference);
        }

        if (build.StateMixinDirectory is { } state)
        {
            Descriptor(state);
        }

        BuildSources.EnsureUnchanged(build);
        return build;
    }

    private async Task PublishSquadAsync(
        PublishRequest request,
        SquadResolver.ResolvedTeam team,
        BuildManifest build,
        IReadOnlyDictionary<string, string> snapshots,
        string directory,
        CancellationToken cancellationToken
    )
    {
        Directory.CreateDirectory(directory);
        var repository =
            $"{Registry}/{build.PublishRepository}/{RepositoryName(team.Name, build.Squad)}";
        var reference = $"{repository}:{request.Tag}";
        var publication = new Publication(
            directory,
            repository,
            "publish-" + Guid.NewGuid().ToString("N"),
            request.Builder
        );
        var kits = await PublishComponentsAsync(publication, build, snapshots, cancellationToken);
        var configDirectory = WriteConfiguration(directory, team.Name, build);
        var config = await PushAsync(
            new PushRequest(
                Directory: configDirectory,
                Reference: $"{repository}/components/config:{publication.Id}",
                Kind: KitKind.Mixin,
                PublicationDirectory: directory
            ),
            builder: request.Builder,
            cancellationToken: cancellationToken
        );
        kits.Add(new { @ref = config });
        var candidate = await PublishCandidateAsync(
            publication,
            team.Name,
            build.Squad,
            kits,
            cancellationToken
        );
        // The requested (possibly moving) tag is untouched until the candidate passes inspection.
        await RunAsync(
            ImageToolsArguments(
                request.Builder,
                ["create", "--prefer-index=false", "--tag", reference, candidate]
            ),
            attachConsole: true,
            cancellationToken: cancellationToken
        );
        await InspectAsync(reference, "workload", cancellationToken, request.Builder);
        console.WriteLine($"Published squad '{build.Squad}' as '{reference}'.");
    }

    private sealed record Publication(
        string Directory,
        string Repository,
        string Id,
        string? Builder
    );

    private async Task<List<object>> PublishComponentsAsync(
        Publication publication,
        BuildManifest build,
        IReadOnlyDictionary<string, string> snapshots,
        CancellationToken cancellationToken
    )
    {
        var kits = new List<object>();
        var workload = await PushAsync(
            new PushRequest(
                Directory: snapshots[build.WorkloadKitDirectory],
                Reference: $"{publication.Repository}/components/workload:{publication.Id}",
                Kind: KitKind.Workload,
                PublicationDirectory: publication.Directory
            ),
            builder: publication.Builder,
            cancellationToken: cancellationToken
        );
        kits.Add(new { @ref = workload });

        for (var index = 0; index < build.Kits.Count; index++)
        {
            var kit = build.Kits[index];
            var source = kit.IsLocal
                ? await PushAsync(
                    new PushRequest(
                        Directory: snapshots[kit.Reference],
                        Reference: $"{publication.Repository}/components/kit-{index}:{publication.Id}",
                        Kind: KitKind.Mixin,
                        PublicationDirectory: publication.Directory
                    ),
                    builder: publication.Builder,
                    cancellationToken: cancellationToken
                )
                : kit.Reference;
            kits.Add(new { @ref = source, args = kit.Arguments });
        }

        if (build.StateMixinDirectory is { } state)
        {
            var source = await PushAsync(
                new PushRequest(
                    Directory: snapshots[state],
                    Reference: $"{publication.Repository}/components/state:{publication.Id}",
                    Kind: KitKind.Mixin,
                    PublicationDirectory: publication.Directory
                ),
                builder: publication.Builder,
                cancellationToken: cancellationToken
            );
            kits.Add(new { @ref = source });
        }

        return kits;
    }

    private static string WriteConfiguration(string directory, string team, BuildManifest build)
    {
        var configDirectory = Path.Combine(directory, "config");
        Directory.CreateDirectory(configDirectory);
        File.WriteAllText(
            Path.Combine(configDirectory, "squad.json"),
            JsonSerializer.Serialize(
                new
                {
                    SchemaVersion = 1,
                    Team = team,
                    build.Squad,
                    build.Tag,
                    build.CreatedAt,
                    build.Mcp,
                    build.State,
                    Kits = build.Kits.Select(kit => new { kit.Name, kit.Arguments }),
                },
                JsonOptions
            )
        );
        WriteDescriptor(
            Path.Combine(configDirectory, "config.yaml"),
            new
            {
                schemaVersion = "3",
                kind = "mixin",
                build = "FROM scratch\nCOPY squad.json /usr/share/abox/squad.json\n",
            }
        );
        return configDirectory;
    }

    private async Task<string> PublishCandidateAsync(
        Publication publication,
        string team,
        string squad,
        IReadOnlyList<object> kits,
        CancellationToken cancellationToken
    )
    {
        var setDirectory = Path.Combine(publication.Directory, "set");
        Directory.CreateDirectory(setDirectory);
        WriteDescriptor(
            Path.Combine(setDirectory, "set.yaml"),
            new
            {
                schemaVersion = "3",
                kind = "set",
                displayName = $"{team} / {squad}",
                kits,
            }
        );
        return await PushAsync(
            new PushRequest(
                Directory: setDirectory,
                Reference: $"{publication.Repository}:{publication.Id}",
                Kind: KitKind.Set,
                PublicationDirectory: publication.Directory
            ),
            builder: publication.Builder,
            cancellationToken: cancellationToken
        );
    }

    private enum KitKind
    {
        Workload,
        Mixin,
        Set,
    }

    private sealed record PushRequest(
        string Directory,
        string Reference,
        KitKind Kind,
        string PublicationDirectory
    );

    private async Task<string> PushAsync(
        PushRequest request,
        string? builder,
        CancellationToken cancellationToken
    )
    {
        console.WriteLine($"Publishing '{request.Reference}'...");
        var metadata = Path.Combine(
            request.PublicationDirectory,
            Guid.NewGuid().ToString("N") + ".json"
        );
        var arguments = new List<string> { "buildx", "build" };
        if (builder is not null)
        {
            arguments.AddRange(["--builder", builder]);
        }

        arguments.AddRange([
            "--file",
            Descriptor(request.Directory),
            "--tag",
            request.Reference,
            "--metadata-file",
            metadata,
            "--provenance=mode=min",
            "--push",
        ]);
        if (
            request.Kind == KitKind.Workload
            && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_TOKEN"))
        )
        {
            arguments.AddRange([
                "--secret",
                $"id={WorkloadKitGenerator.GitHubTokenSecret},env=GITHUB_TOKEN",
            ]);
        }

        arguments.Add(request.Directory);
        await RunAsync(arguments, attachConsole: true, cancellationToken: cancellationToken);
        string digest;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(metadata));
            digest = document.RootElement.GetProperty("containerimage.digest").GetString() ?? "";
            if (!OciRegistry.IsDigest(digest))
            {
                throw new JsonException("Missing or invalid image digest.");
            }
        }
        catch (Exception exception)
            when (exception
                    is JsonException
                        or KeyNotFoundException
                        or InvalidOperationException
                        or IOException
            )
        {
            throw new AboxException(
                $"Cannot read the pushed digest for '{request.Reference}'; the requested set tag was not promoted.",
                exception
            );
        }

        var pinned = OciRegistry.Pin(request.Reference, digest);
        await InspectAsync(
            pinned,
            request.Kind == KitKind.Mixin ? "mixin" : "workload",
            cancellationToken,
            builder
        );
        return pinned;
    }

    internal async Task<IReadOnlyList<JsonElement>> InspectAsync(
        string reference,
        string kind,
        CancellationToken cancellationToken,
        string? builder = null,
        int depth = 0
    )
    {
        if (depth > 4)
            throw new AboxException(
                $"Registry artifact '{reference}' has too many nested image indexes."
            );

        var result = CheckResult(
            await registry.InspectAsync(reference, true, builder, cancellationToken)
        );
        try
        {
            using var document = JsonDocument.Parse(result.StandardOutput);
            var root = document.RootElement;
            if (root.GetProperty("schemaVersion").GetInt32() != 2)
                throw new JsonException("Expected an OCI image manifest or index.");
            if (!root.TryGetProperty("manifests", out var manifests))
                return [OciRegistry.Descriptor(root, kind, $"Expected a v3 {kind} descriptor.")];

            var leaves = new List<JsonElement>();
            foreach (var manifest in manifests.EnumerateArray())
            {
                if (
                    manifest.TryGetProperty("annotations", out var annotations)
                    && annotations.TryGetProperty("vnd.docker.reference.type", out var type)
                    && type.GetString() == "attestation-manifest"
                    && manifest.TryGetProperty("platform", out var platform)
                    && platform.GetProperty("os").GetString() == "unknown"
                    && platform.GetProperty("architecture").GetString() == "unknown"
                )
                    continue;
                var digest = manifest.GetProperty("digest").GetString() ?? "";
                if (!OciRegistry.IsDigest(digest))
                    throw new JsonException("Invalid platform manifest digest.");
                leaves.AddRange(
                    await InspectAsync(
                        OciRegistry.Repository(reference) + "@" + digest,
                        kind,
                        cancellationToken,
                        builder,
                        depth + 1
                    )
                );
            }
            if (leaves.Count == 0)
                throw new JsonException("No runnable platform manifests.");
            return leaves;
        }
        catch (Exception exception)
            when (exception
                    is JsonException
                        or KeyNotFoundException
                        or InvalidOperationException
                        or FormatException
            )
        {
            throw new AboxException(
                $"Registry artifact '{reference}' did not retain a valid v3 {kind} descriptor. Publication stopped: {exception.Message}",
                exception
            );
        }
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
            {
                throw new AboxException(
                    $"Kit argument '{name}' is undeclared or build-phase. Publishing supplied build-phase arguments is not supported; only create-phase arguments may be fixed in a set."
                );
            }
        }
    }

    private static void ValidateRegistryArguments(
        JsonElement descriptor,
        IReadOnlyDictionary<string, string> arguments
    )
    {
        foreach (var name in arguments.Keys)
        {
            if (
                !descriptor.TryGetProperty("args", out var declarations)
                || !declarations.TryGetProperty(name, out var declaration)
                || declaration.TryGetProperty("buildArg", out _)
                || declaration.TryGetProperty("buildSecret", out _)
            )
            {
                throw new AboxException(
                    $"Registry kit argument '{name}' is undeclared or build-phase. Publishing supplied build-phase arguments is not supported."
                );
            }
        }
    }

    private async Task<ProcessResult> RunAsync(
        IReadOnlyList<string> arguments,
        bool attachConsole,
        CancellationToken cancellationToken
    ) => CheckResult(await registry.RunAsync(arguments, attachConsole, cancellationToken));

    private static ProcessResult CheckResult(ProcessResult result)
    {
        if (!result.Succeeded)
            throw new AboxException(
                $"Publishing failed (docker buildx, exit code {result.ExitCode}). Check the Buildx builder and run 'docker login {RegistryImageName.Registry}' with a JFrog identity allowed to push to the workload's publishRepository.{Environment.NewLine}{(result.StandardError + result.StandardOutput).Trim()}"
            );
        return result;
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
        {
            throw new AboxException(
                "Selected team or squad names collide when compared without case. Give every team and squad a distinct registry identity."
            );
        }

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
            {
                throw new AboxException(
                    "Team folders or squad files have names that collide without case; rename them before publishing."
                );
            }
        }
    }

    private static string RepositoryName(string team, string squad) =>
        RegistryImageName.SquadRepository(team, squad);

    private static string Descriptor(string directory) =>
        KitDescriptor.Load(directory)?.DescriptorPath
        ?? throw new AboxException(
            $"The built kit at '{directory}' has no descriptor. Run 'abox build' again."
        );

    private static IReadOnlyList<string> ImageToolsArguments(
        string? builder,
        IReadOnlyList<string> arguments
    ) => OciRegistry.ImageToolsArguments(builder, arguments);

    private static void WriteDescriptor(string path, object descriptor) =>
        File.WriteAllText(
            path,
            "# syntax=docker/sandbox-kit:3\n" + JsonSerializer.Serialize(descriptor, JsonOptions)
        );
}
