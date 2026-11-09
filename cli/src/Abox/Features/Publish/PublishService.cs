using System.Text.Json;
using System.Text.RegularExpressions;
using Abox.Build;
using Abox.Features.Build;
using Abox.Infrastructure;
using Abox.Kits;
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
    IProcessRunner runner,
    IConsole console
)
{
    public const string Registry = RegistryImageName.Registry;
    private const string DescriptorAnnotation = "vnd.docker.sandbox.kit.descriptor";
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

        var directory = Path.Combine(
            Path.GetTempPath(),
            "abox-publish-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        try
        {
            // Snapshot every team's validated inputs before the first registry write.
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
                {
                    ValidateLocalArguments(snapshots[index][kit.Reference], kit.Arguments);
                }
            }

            await RunAsync(["buildx", "version"], false, cancellationToken);
            foreach (
                var kit in builds.SelectMany(item => item.Build.Kits).Where(kit => !kit.IsLocal)
            )
            {
                var descriptors = await InspectAsync(
                    kit.Reference,
                    "mixin",
                    cancellationToken,
                    request.Builder
                );
                foreach (var descriptor in descriptors)
                {
                    ValidateRegistryArguments(descriptor, kit.Arguments);
                }
            }

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
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException)
            {
                console.WriteError(
                    $"warning: could not remove publication directory '{directory}': {exception.Message}"
                );
            }
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
        var publication = "publish-" + Guid.NewGuid().ToString("N");
        var kits = new List<object>();
        var workload = await PushAsync(
            snapshots[build.WorkloadKitDirectory],
            $"{repository}/components/workload:{publication}",
            "workload",
            request,
            directory,
            true,
            cancellationToken
        );
        kits.Add(new { @ref = workload });

        for (var index = 0; index < build.Kits.Count; index++)
        {
            var kit = build.Kits[index];
            var source = kit.IsLocal
                ? await PushAsync(
                    snapshots[kit.Reference],
                    $"{repository}/components/kit-{index}:{publication}",
                    "mixin",
                    request,
                    directory,
                    false,
                    cancellationToken
                )
                : kit.Reference;
            kits.Add(new { @ref = source, args = kit.Arguments });
        }

        if (build.StateMixinDirectory is { } state)
        {
            var source = await PushAsync(
                snapshots[state],
                $"{repository}/components/state:{publication}",
                "mixin",
                request,
                directory,
                false,
                cancellationToken
            );
            kits.Add(new { @ref = source });
        }

        var configDirectory = Path.Combine(directory, "config");
        Directory.CreateDirectory(configDirectory);
        File.WriteAllText(
            Path.Combine(configDirectory, "squad.json"),
            JsonSerializer.Serialize(
                new
                {
                    Team = team.Name,
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
        var config = await PushAsync(
            configDirectory,
            $"{repository}/components/config:{publication}",
            "mixin",
            request,
            directory,
            false,
            cancellationToken
        );
        kits.Add(new { @ref = config });

        var setDirectory = Path.Combine(directory, "set");
        Directory.CreateDirectory(setDirectory);
        WriteDescriptor(
            Path.Combine(setDirectory, "set.yaml"),
            new
            {
                schemaVersion = "3",
                kind = "set",
                displayName = $"{team.Name} / {build.Squad}",
                kits,
            }
        );
        var candidate = await PushAsync(
            setDirectory,
            $"{repository}:{publication}",
            "workload",
            request,
            directory,
            false,
            cancellationToken
        );
        // The requested (possibly moving) tag is untouched until the candidate passes inspection.
        await RunAsync(
            ImageToolsArguments(
                request.Builder,
                ["create", "--prefer-index=false", "--tag", reference, candidate]
            ),
            true,
            cancellationToken
        );
        await InspectAsync(reference, "workload", cancellationToken, request.Builder);
        console.WriteLine($"Published squad '{build.Squad}' as '{reference}'.");
    }

    private async Task<string> PushAsync(
        string directory,
        string reference,
        string kind,
        PublishRequest request,
        string publicationDirectory,
        bool workload,
        CancellationToken cancellationToken
    )
    {
        console.WriteLine($"Publishing '{reference}'...");
        var metadata = Path.Combine(publicationDirectory, Guid.NewGuid().ToString("N") + ".json");
        var arguments = new List<string> { "buildx", "build" };
        if (request.Builder is { } builder)
        {
            arguments.AddRange(["--builder", builder]);
        }

        arguments.AddRange([
            "--file",
            Descriptor(directory),
            "--tag",
            reference,
            "--metadata-file",
            metadata,
            "--provenance=mode=min",
            "--push",
        ]);
        if (workload && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_TOKEN")))
        {
            arguments.AddRange([
                "--secret",
                $"id={WorkloadKitGenerator.GitHubTokenSecret},env=GITHUB_TOKEN",
            ]);
        }

        arguments.Add(directory);
        await RunAsync(arguments, true, cancellationToken);
        string digest;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(metadata));
            digest = document.RootElement.GetProperty("containerimage.digest").GetString() ?? "";
            if (!Regex.IsMatch(digest, @"\Asha256:[a-f0-9]{64}\z"))
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
                $"Cannot read the pushed digest for '{reference}'; the requested set tag was not promoted.",
                exception
            );
        }

        var pinned = reference[..reference.LastIndexOf(':')] + "@" + digest;
        await InspectAsync(pinned, kind, cancellationToken, request.Builder);
        return pinned;
    }

    private async Task<IReadOnlyList<JsonElement>> InspectAsync(
        string reference,
        string kind,
        CancellationToken cancellationToken,
        string? builder = null,
        int depth = 0
    )
    {
        if (depth > 4)
        {
            throw new AboxException(
                $"Registry artifact '{reference}' has too many nested image indexes."
            );
        }

        var result = await RunAsync(
            ImageToolsArguments(builder, ["inspect", "--raw", reference]),
            false,
            cancellationToken
        );
        try
        {
            using var document = JsonDocument.Parse(result.StandardOutput);
            var root = document.RootElement;
            if (root.GetProperty("schemaVersion").GetInt32() != 2)
            {
                throw new JsonException("Expected an OCI image manifest or index.");
            }
            if (root.TryGetProperty("manifests", out var manifests))
            {
                var descriptors = new List<JsonElement>();
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
                    {
                        continue;
                    }

                    var digest = manifest.GetProperty("digest").GetString() ?? "";
                    if (!Regex.IsMatch(digest, @"\Asha256:[a-f0-9]{64}\z"))
                    {
                        throw new JsonException("Invalid platform manifest digest.");
                    }

                    var repository = reference.Split('@')[0];
                    var colon = repository.LastIndexOf(':');
                    if (colon > repository.LastIndexOf('/'))
                    {
                        repository = repository[..colon];
                    }

                    descriptors.AddRange(
                        await InspectAsync(
                            repository + "@" + digest,
                            kind,
                            cancellationToken,
                            builder,
                            depth + 1
                        )
                    );
                }

                if (descriptors.Count == 0)
                {
                    throw new JsonException("No runnable platform manifests.");
                }

                return descriptors;
            }

            using var descriptor = JsonDocument.Parse(
                root.GetProperty("annotations").GetProperty(DescriptorAnnotation).GetString() ?? ""
            );
            if (
                descriptor.RootElement.GetProperty("schemaVersion").GetString() != "3"
                || descriptor.RootElement.GetProperty("kind").GetString() != kind
            )
            {
                throw new JsonException($"Expected a v3 {kind} descriptor.");
            }

            return [descriptor.RootElement.Clone()];
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
    )
    {
        var result = await runner.RunAsync(
            new ProcessRequest("docker", arguments, AttachConsole: attachConsole),
            cancellationToken
        );
        if (!result.Succeeded)
        {
            throw new AboxException(
                $"Publishing failed (docker buildx, exit code {result.ExitCode}). Check the Buildx builder and run 'docker login {Registry}' with a JFrog identity allowed to push to the workload's publishRepository.{Environment.NewLine}{(result.StandardError + result.StandardOutput).Trim()}"
            );
        }

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
    )
    {
        var result = new List<string> { "buildx", "imagetools" };
        if (builder is not null)
        {
            result.AddRange(["--builder", builder]);
        }

        result.AddRange(arguments);
        return result;
    }

    private static void WriteDescriptor(string path, object descriptor) =>
        File.WriteAllText(
            path,
            "# syntax=docker/sandbox-kit:3\n" + JsonSerializer.Serialize(descriptor, JsonOptions)
        );
}
