using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Abox.Infrastructure;
using Abox.Kits;
using Abox.LocalState;
using Abox.Naming;

namespace Abox.Sbx;

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
        using var temporary = new TemporaryDirectory("abox-pull-", "download", console);
        var directory = temporary.Path;
        try
        {
            var (digest, manifest) = await registry.ResolveManifestAsync(
                reference,
                architecture,
                cancellationToken
            );
            var pinned = OciRegistry.Pin(reference, digest);
            var descriptor = Descriptor(manifest, "workload");
            var config = ConfigurationReference(descriptor, team, squad);
            Descriptor(
                (
                    await registry.ResolveManifestAsync(config, architecture, cancellationToken)
                ).Manifest,
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

    private static JsonElement Descriptor(JsonElement manifest, string kind) =>
        OciRegistry.Descriptor(manifest, kind, "Expected a processed v3 kit descriptor.");

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
        ValidateIdentity(root, team, squad, tag);
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
            throw new JsonException("Incomplete or duplicate runtime metadata.");
        ValidateState(state, descriptor);
        ValidateMcp(mcp);
        return new BuildManifest
        {
            Squad = squad,
            Tag = tag,
            CreatedAt = createdAt,
            State = state,
            Mcp = mcp,
        };
    }

    private static void ValidateIdentity(JsonElement root, string team, string squad, string tag)
    {
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
    }

    private static void ValidateState(IReadOnlyList<ManifestState> state, JsonElement descriptor)
    {
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
    }

    private static void ValidateMcp(IReadOnlyList<ManifestMcp> mcp)
    {
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

    private Task<ProcessResult> RunAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken
    ) => registry.RunAsync(arguments, DockerOperation.MetadataRead, cancellationToken);
}
