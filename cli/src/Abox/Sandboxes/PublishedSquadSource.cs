using System.Runtime.InteropServices;
using System.Text.Json;
using Abox.Infrastructure;
using Abox.LocalState;
using Abox.Naming;

namespace Abox.Sandboxes;

/// <summary>A pinned squad set and its authoritative runtime metadata.</summary>
public sealed record PublishedSquad(string Reference, BuildManifest Metadata);

/// <summary>Reads authoritative runtime metadata from a processed, digest-pinned squad set.</summary>
public interface IPublishedSquadSource
{
    Task<PublishedSquad> LoadAsync(
        string reference,
        string team,
        string squad,
        string tag,
        CancellationToken cancellationToken
    );
}

/// <summary>Only registry reads: inspect the set, pull its small config mixin, and read its saved layers without executing or extracting them.</summary>
public sealed class PublishedSquadSource(
    IOciRegistry registry,
    IHostPrograms programs,
    IConsole console
) : IPublishedSquadSource
{
    public async Task<PublishedSquad> LoadAsync(
        string reference,
        string team,
        string squad,
        string tag,
        CancellationToken cancellationToken
    )
    {
        OciRegistry.RequireDocker(
            programs,
            $"Remote create needs Docker with Buildx to read the published squad metadata. Install Docker and run 'docker login {RegistryImageName.Registry}' with read access; no local build or team sources are needed."
        );

        using var temporary = new TemporaryDirectory("abox-pull-", "download", console);
        try
        {
            return await ReadPinnedAsync(
                reference,
                new PublishedRuntimeMetadata.Identity(team, squad, tag),
                temporary.Path,
                cancellationToken
            );
        }
        catch (Exception exception)
            when (exception
                    is JsonException
                        or KeyNotFoundException
                        or InvalidOperationException
                        or IOException
                        or InvalidDataException
                        or NotSupportedException
                        or UnauthorizedAccessException
                        or ArgumentException
                        or FormatException
            )
        {
            throw new AboxException(
                $"The published set '{reference}' has missing, incompatible or invalid bundled squad metadata. Ask a maintainer to rebuild with 'abox build --team {team} --tag {tag} --no-personal' and republish this tag with the current abox. No local metadata fallback is used; the existing sandbox was not replaced.",
                exception
            );
        }
    }

    private async Task<PublishedSquad> ReadPinnedAsync(
        string reference,
        PublishedRuntimeMetadata.Identity identity,
        string directory,
        CancellationToken cancellationToken
    )
    {
        var architecture =
            RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "amd64";
        var (digest, manifest) = await ResolveManifestAsync(
            reference,
            architecture,
            cancellationToken
        );
        var pinned = OciRegistry.Pin(reference, digest);
        var descriptor = Descriptor(manifest, "workload");
        var config = ConfigurationReference(descriptor, identity.Team, identity.Squad);
        Descriptor(
            (await ResolveManifestAsync(config, architecture, cancellationToken)).Manifest,
            "mixin"
        );
        await RunAsync(["pull", "--platform", "linux/" + architecture, config], cancellationToken);
        var archive = Path.Combine(directory, "config.tar");
        await RunAsync(["image", "save", "--output", archive, config], cancellationToken);
        var metadata = PublishedRuntimeMetadata.Read(
            PublishedConfigArchive.Read(archive),
            identity,
            descriptor
        );
        return new PublishedSquad(pinned, metadata);
    }

    private static string ConfigurationReference(JsonElement descriptor, string team, string squad)
    {
        var suffix = "/" + RegistryImageName.SquadRepository(team, squad) + "/components/config@";
        var configs = descriptor
            .GetProperty("kits")
            .EnumerateArray()
            .Select(kit => kit.GetProperty("ref").GetString() ?? "")
            .Where(value =>
                value.StartsWith(RegistryImageName.Registry + "/", StringComparison.Ordinal)
                && value.Contains(suffix, StringComparison.Ordinal)
            )
            .ToList();
        if (configs.Count != 1)
            throw new JsonException("Expected exactly one bundled squad configuration mixin.");
        var config = configs[0];
        var prefix = config[..config.IndexOf(suffix, StringComparison.Ordinal)];
        if (!RegistryImageName.IsValidRepository(prefix[(RegistryImageName.Registry.Length + 1)..]))
            throw new JsonException("Invalid configuration repository.");
        OciRegistry.Digest(
            config[(config.IndexOf(suffix, StringComparison.Ordinal) + suffix.Length)..]
        );
        return config;
    }

    private sealed record OciManifest(string Digest, JsonElement Manifest);

    // Remote consumption selects only this host's Linux image. Publication deliberately verifies all platforms.
    private async Task<OciManifest> ResolveManifestAsync(
        string reference,
        string architecture,
        CancellationToken cancellationToken,
        int depth = 0
    )
    {
        if (depth > 4)
            throw new JsonException("Too many nested image indexes.");

        string digest;
        if (reference.Contains('@'))
            digest = OciRegistry.Digest(reference[(reference.LastIndexOf('@') + 1)..]);
        else
        {
            var resolved = CheckResult(
                await registry.InspectAsync(reference, false, null, cancellationToken),
                "buildx"
            );
            using var identity = JsonDocument.Parse(resolved.StandardOutput);
            digest = OciRegistry.Digest(identity.RootElement.GetProperty("digest").GetString());
        }
        var result = CheckResult(
            await registry.InspectAsync(
                OciRegistry.Repository(reference) + "@" + digest,
                true,
                null,
                cancellationToken
            ),
            "buildx"
        );
        var root = OciRegistry.ParseManifest(result.StandardOutput);
        if (!root.TryGetProperty("manifests", out var manifests))
            return new OciManifest(digest, root);

        var selected = manifests
            .EnumerateArray()
            .Where(manifest =>
                manifest.TryGetProperty("platform", out var platform)
                && platform.GetProperty("os").GetString() == "linux"
                && platform.GetProperty("architecture").GetString() == architecture
            )
            .ToList();
        if (selected.Count != 1)
            throw new JsonException("No unambiguous runnable platform manifest.");
        var childDigest = OciRegistry.Digest(selected[0].GetProperty("digest").GetString());
        return await ResolveManifestAsync(
            OciRegistry.Repository(reference) + "@" + childDigest,
            architecture,
            cancellationToken,
            depth + 1
        );
    }

    private static JsonElement Descriptor(JsonElement manifest, string kind) =>
        OciRegistry.Descriptor(manifest, kind, "Expected a processed v3 kit descriptor.");

    private async Task<ProcessResult> RunAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken
    ) => CheckResult(await registry.RunAsync(arguments, false, cancellationToken), arguments[0]);

    private static ProcessResult CheckResult(ProcessResult result, string command)
    {
        // Docker handles credentials; metadata reads must never echo their output.
        if (!result.Succeeded)
            throw new AboxException(
                $"Cannot read the published squad metadata (docker {command}, exit code {result.ExitCode}). Check the published tag and Docker/Buildx, and run 'docker login {RegistryImageName.Registry}' with read access to the set and its bundled components. For an older artifact, ask a maintainer to republish it with current abox. The existing sandbox was not replaced."
            );
        return result;
    }
}
