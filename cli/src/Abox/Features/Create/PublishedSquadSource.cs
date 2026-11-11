using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Abox.Build;
using Abox.Infrastructure;
using Abox.Kits;
using Abox.Naming;

namespace Abox.Features.Create;

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
    IProcessRunner runner,
    IHostPrograms programs,
    IConsole console
) : IPublishedSquadSource
{
    private const string DescriptorAnnotation = "vnd.docker.sandbox.kit.descriptor";
    private const int MaximumFileSize = 16 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        RespectNullableAnnotations = true,
    };

    public async Task<PublishedSquad> LoadAsync(
        string reference,
        string team,
        string squad,
        string tag,
        CancellationToken cancellationToken
    )
    {
        if (!programs.Exists("docker"))
        {
            throw new AboxException(
                $"Remote create needs Docker with Buildx to read the published squad metadata. Install Docker and run 'docker login {RegistryImageName.Registry}' with read access; no local build or team sources are needed."
            );
        }

        var architecture =
            RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "amd64";
        var platform = "linux/" + architecture;
        var directory = Path.Combine(
            Path.GetTempPath(),
            "abox-pull-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        try
        {
            var (digest, manifest) = await InspectAsync(reference, architecture, cancellationToken);
            var pinned = reference[..reference.LastIndexOf(':')] + "@" + digest;
            var descriptor = Descriptor(manifest, "workload");
            var suffix =
                "/" + RegistryImageName.SquadRepository(team, squad) + "/components/config@";
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
            {
                throw new JsonException("Expected exactly one bundled squad configuration mixin.");
            }
            var config = configs[0];
            var prefix = config[..config.IndexOf(suffix, StringComparison.Ordinal)];
            if (
                !RegistryImageName.IsValidRepository(
                    prefix[(RegistryImageName.Registry.Length + 1)..]
                )
            )
            {
                throw new JsonException("Invalid configuration repository.");
            }
            Digest(config[(config.IndexOf(suffix, StringComparison.Ordinal) + suffix.Length)..]);
            Descriptor(
                (await InspectAsync(config, architecture, cancellationToken)).Manifest,
                "mixin"
            );
            await RunAsync(["pull", "--platform", platform, config], cancellationToken);
            var archive = Path.Combine(directory, "config.tar");
            await RunAsync(["image", "save", "--output", archive, config], cancellationToken);
            var metadata = ReadMetadata(ReadSquadJson(archive), team, squad, tag, descriptor);
            return new PublishedSquad(pinned, metadata);
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
                    $"warning: could not remove download directory '{directory}': {exception.Message}"
                );
            }
        }
    }

    private async Task<(string Digest, JsonElement Manifest)> InspectAsync(
        string reference,
        string architecture,
        CancellationToken cancellationToken,
        int depth = 0
    )
    {
        if (depth > 4)
        {
            throw new JsonException("Too many nested image indexes.");
        }
        string digest;
        var repository = reference.Split('@')[0];
        if (reference.Contains('@'))
        {
            digest = Digest(reference[(reference.LastIndexOf('@') + 1)..]);
        }
        else
        {
            var resolved = await RunAsync(
                ["buildx", "imagetools", "inspect", "--format", "{{json .Manifest}}", reference],
                cancellationToken
            );
            using var identity = JsonDocument.Parse(resolved.StandardOutput);
            digest = Digest(identity.RootElement.GetProperty("digest").GetString());
        }
        if (repository.LastIndexOf(':') > repository.LastIndexOf('/'))
        {
            repository = repository[..repository.LastIndexOf(':')];
        }
        var result = await RunAsync(
            ["buildx", "imagetools", "inspect", "--raw", repository + "@" + digest],
            cancellationToken
        );
        using var document = JsonDocument.Parse(result.StandardOutput);
        var root = document.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 2)
        {
            throw new JsonException("Expected an OCI image manifest or index.");
        }
        if (!root.TryGetProperty("manifests", out var manifests))
        {
            return (digest, root.Clone());
        }
        var selected = manifests
            .EnumerateArray()
            .Where(item =>
                item.TryGetProperty("platform", out var platform)
                && platform.GetProperty("os").GetString() == "linux"
                && platform.GetProperty("architecture").GetString() == architecture
            )
            .ToList();
        if (selected.Count != 1)
        {
            throw new JsonException("No unambiguous runnable platform manifest.");
        }
        return await InspectAsync(
            repository + "@" + Digest(selected[0].GetProperty("digest").GetString()),
            architecture,
            cancellationToken,
            depth + 1
        );
    }

    private static JsonElement Descriptor(JsonElement manifest, string kind)
    {
        using var document = JsonDocument.Parse(
            manifest.GetProperty("annotations").GetProperty(DescriptorAnnotation).GetString() ?? ""
        );
        var descriptor = document.RootElement;
        if (
            descriptor.GetProperty("schemaVersion").GetString() != "3"
            || descriptor.GetProperty("kind").GetString() != kind
        )
        {
            throw new JsonException("Expected a processed v3 kit descriptor.");
        }
        return descriptor.Clone();
    }

    private static string Digest(string? value) =>
        value is not null && Regex.IsMatch(value, @"\Asha256:[a-f0-9]{64}\z")
            ? value
            : throw new JsonException("Invalid published digest.");

    private static BuildManifest ReadMetadata(
        string json,
        string team,
        string squad,
        string tag,
        JsonElement descriptor
    )
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        // The original publisher omitted SchemaVersion; its otherwise identical format is version 1.
        if (
            (root.TryGetProperty("SchemaVersion", out var version) && version.GetInt32() != 1)
            || !string.Equals(
                root.GetProperty("Team").GetString(),
                team,
                StringComparison.OrdinalIgnoreCase
            )
            || !string.Equals(
                root.GetProperty("Squad").GetString(),
                squad,
                StringComparison.OrdinalIgnoreCase
            )
            || root.GetProperty("Tag").GetString() != tag
        )
        {
            throw new JsonException("Squad metadata schema or identity mismatch.");
        }
        var state =
            root.GetProperty("State").Deserialize<List<ManifestState>>(JsonOptions)
            ?? throw new JsonException("Missing State.");
        var mcp =
            root.GetProperty("Mcp").Deserialize<List<ManifestMcp>>(JsonOptions)
            ?? throw new JsonException("Missing Mcp.");
        var createdAt = root.GetProperty("CreatedAt").GetDateTimeOffset();
        if (
            createdAt == default
            || state.Any(item => item is null)
            || mcp.Any(item => item is null)
            || state.DistinctBy(item => item.SandboxPath).Count() != state.Count
            || mcp.DistinctBy(item => item.Name).Count() != mcp.Count
        )
        {
            throw new JsonException("Incomplete or duplicate runtime metadata.");
        }
        foreach (var entry in state)
        {
            if (
                !SafeRelative(entry.RelativePath)
                || !entry.SandboxPath.StartsWith('/')
                || !SafeRelative(entry.SandboxPath[1..])
                || (
                    entry.SandboxPath.StartsWith(
                        StateEntry.SandboxHome + "/",
                        StringComparison.Ordinal
                    )
                        ? entry.SandboxPath[(StateEntry.SandboxHome.Length + 1)..]
                        : entry.SandboxPath[1..]
                ) != entry.RelativePath
                || !descriptor
                    .GetProperty("capabilities")
                    .EnumerateArray()
                    .Any(capability =>
                        capability.GetProperty("type").GetString()
                            == "com.docker.sandbox/host-mount@1"
                        && capability.GetProperty("config").GetProperty("path").GetString()
                            == entry.SandboxPath
                    )
            )
            {
                throw new JsonException("Unsafe state path or missing host-mount declaration.");
            }
        }
        foreach (var server in mcp)
        {
            if (
                !Regex.IsMatch(server.Name, @"\A[A-Za-z0-9_][A-Za-z0-9_.-]*\z")
                || (
                    server.Url is not null
                    && (
                        !Uri.TryCreate(server.Url, UriKind.Absolute, out var url)
                        || url.Scheme is not ("http" or "https")
                        || !string.IsNullOrEmpty(url.UserInfo)
                    )
                )
            )
            {
                throw new JsonException("Invalid MCP declaration.");
            }
        }
        return new BuildManifest
        {
            Squad = squad,
            Tag = tag,
            CreatedAt = createdAt,
            State = state,
            Mcp = mcp,
        };
    }

    private static bool SafeRelative(string value) =>
        !string.IsNullOrWhiteSpace(value)
        && !value.Contains('\\')
        && !value.Contains(':')
        && value
            .Split('/')
            .All(part =>
                part.Length > 0
                && part is not ("." or "..")
                && part.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
            );

    private static string ReadSquadJson(string archive)
    {
        // Docker save supplies manifest.json and an ordered Layers list on both classic and containerd stores.
        using var manifestStream = File.OpenRead(archive);
        using var manifestReader = new TarReader(manifestStream);
        List<string>? layers = null;
        while (manifestReader.GetNextEntry() is { } entry)
        {
            if (entry.Name == "manifest.json")
            {
                using var document = JsonDocument.Parse(ReadBytes(entry));
                var images = document.RootElement.EnumerateArray().ToList();
                if (images.Count != 1)
                    throw new JsonException("Expected one config image.");
                layers = images[0]
                    .GetProperty("Layers")
                    .EnumerateArray()
                    .Select(layer => layer.GetString()!)
                    .ToList();
                break;
            }
        }
        if (layers is null)
            throw new JsonException("Missing Docker save manifest.");
        var content = new Dictionary<string, byte[]>();
        using var stream = File.OpenRead(archive);
        using var reader = new TarReader(stream);
        while (reader.GetNextEntry() is { } entry)
        {
            if (layers.Contains(entry.Name))
                content.Add(entry.Name, ReadBytes(entry));
        }
        string? json = null;
        foreach (var layer in layers)
        {
            using var bytes = new MemoryStream(content[layer]);
            using Stream decoded = content[layer] is [0x1f, 0x8b, ..]
                ? new GZipStream(bytes, CompressionMode.Decompress)
                : bytes;
            using var tar = new TarReader(decoded);
            while (tar.GetNextEntry() is { } entry)
            {
                var name = entry.Name.TrimStart('/');
                if (name.StartsWith("./", StringComparison.Ordinal))
                    name = name[2..];
                if (name == "usr/share/abox/squad.json")
                {
                    if (
                        entry.EntryType
                        is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile)
                    )
                        throw new JsonException("Metadata must be a regular file.");
                    json = System.Text.Encoding.UTF8.GetString(ReadBytes(entry));
                }
                if (name is "usr/share/abox/.wh.squad.json" or "usr/share/abox/.wh..wh..opq")
                    json = null;
            }
        }
        return json ?? throw new JsonException("No bundled squad.json.");
    }

    private static byte[] ReadBytes(TarEntry entry)
    {
        if (entry.Length > MaximumFileSize || entry.DataStream is null)
            throw new JsonException("Invalid metadata layer size.");
        using var result = new MemoryStream();
        entry.DataStream.CopyTo(result);
        return result.ToArray();
    }

    private async Task<ProcessResult> RunAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken
    )
    {
        var result = await runner.RunAsync(
            new ProcessRequest("docker", arguments),
            cancellationToken
        );
        if (!result.Succeeded)
        {
            // Docker handles credentials; never echo credential helpers, tokens or their output in errors.
            throw new AboxException(
                $"Cannot read the published squad metadata (docker {arguments[0]}, exit code {result.ExitCode}). Check the published tag and Docker/Buildx, and run 'docker login {RegistryImageName.Registry}' with read access to the set and its bundled components. For an older artifact, ask a maintainer to republish it with current abox. The existing sandbox was not replaced."
            );
        }
        return result;
    }
}
