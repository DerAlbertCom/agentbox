using System.Text.Json;
using System.Text.RegularExpressions;
using Abox.Naming;

namespace Abox.Infrastructure;

/// <summary>The Docker operation whose existing failure policy applies.</summary>
public enum DockerOperation
{
    Publication,
    MetadataRead,
    LocalWorkload,
}

/// <summary>A resolved platform manifest and its content identity.</summary>
public sealed record OciManifest(string Digest, JsonElement Manifest);

/// <summary>The shared entry point for Docker operations and processed OCI kit inspection.</summary>
public interface IOciRegistry
{
    Task<ProcessResult> RunAsync(
        IReadOnlyList<string> arguments,
        DockerOperation operation,
        CancellationToken cancellationToken,
        bool attachConsole = false
    );

    Task<IReadOnlyList<JsonElement>> InspectDescriptorsAsync(
        string reference,
        string kind,
        CancellationToken cancellationToken,
        string? builder = null
    );

    Task<OciManifest> ResolveManifestAsync(
        string reference,
        string architecture,
        CancellationToken cancellationToken
    );
}

/// <summary>Inspects all publication platforms or the single runtime platform without mixing their failure policies.</summary>
public sealed class OciRegistry(IProcessRunner runner) : IOciRegistry
{
    private const string DescriptorAnnotation = "vnd.docker.sandbox.kit.descriptor";
    private static readonly Regex DigestPattern = new(@"\Asha256:[a-f0-9]{64}\z");

    public static bool IsDigest(string? value) => value is not null && DigestPattern.IsMatch(value);

    public static string Digest(string? value) =>
        IsDigest(value) ? value! : throw new JsonException("Invalid published digest.");

    public static string Pin(string reference, string digest) =>
        reference[..reference.LastIndexOf(':')] + "@" + digest;

    private static string Repository(string reference)
    {
        var repository = reference.Split('@')[0];
        var colon = repository.LastIndexOf(':');
        return colon > repository.LastIndexOf('/') ? repository[..colon] : repository;
    }

    public static JsonElement Descriptor(JsonElement manifest, string kind, string error)
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
            throw new JsonException(error);
        }
        return descriptor.Clone();
    }

    public static IReadOnlyList<string> ImageToolsArguments(
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

    public async Task<ProcessResult> RunAsync(
        IReadOnlyList<string> arguments,
        DockerOperation operation,
        CancellationToken cancellationToken,
        bool attachConsole = false
    )
    {
        var result = await runner.RunAsync(
            new ProcessRequest("docker", arguments, AttachConsole: attachConsole),
            cancellationToken
        );
        if (!result.Succeeded)
        {
            throw new AboxException(
                operation switch
                {
                    DockerOperation.Publication =>
                        $"Publishing failed (docker buildx, exit code {result.ExitCode}). Check the Buildx builder and run 'docker login {RegistryImageName.Registry}' with a JFrog identity allowed to push to the workload's publishRepository.{Environment.NewLine}{(result.StandardError + result.StandardOutput).Trim()}",
                    // Docker handles credentials; metadata reads must never echo their output.
                    DockerOperation.MetadataRead =>
                        $"Cannot read the published squad metadata (docker {arguments[0]}, exit code {result.ExitCode}). Check the published tag and Docker/Buildx, and run 'docker login {RegistryImageName.Registry}' with read access to the set and its bundled components. For an older artifact, ask a maintainer to republish it with current abox. The existing sandbox was not replaced.",
                    _ =>
                        $"Local workload image operation failed (Docker, exit {result.ExitCode}). Check the Docker context and run 'abox build' again. No source-build fallback is performed.{Environment.NewLine}{(result.StandardError + result.StandardOutput).Trim()}",
                }
            );
        }
        return result;
    }

    public async Task<IReadOnlyList<JsonElement>> InspectDescriptorsAsync(
        string reference,
        string kind,
        CancellationToken cancellationToken,
        string? builder = null
    ) =>
        (await InspectAsync(reference, new Inspection(kind, null, builder), cancellationToken))
            .Select(item => item.Manifest)
            .ToList();

    public async Task<OciManifest> ResolveManifestAsync(
        string reference,
        string architecture,
        CancellationToken cancellationToken
    ) =>
        (
            await InspectAsync(
                reference,
                new Inspection(null, architecture, null),
                cancellationToken
            )
        )[0];

    private sealed record Inspection(string? Kind, string? Architecture, string? Builder)
    {
        public bool IsPublication => Kind is not null;
    }

    private async Task<IReadOnlyList<OciManifest>> InspectAsync(
        string reference,
        Inspection inspection,
        CancellationToken cancellationToken,
        int depth = 0
    )
    {
        if (depth > 4)
        {
            if (inspection.IsPublication)
                throw new AboxException(
                    $"Registry artifact '{reference}' has too many nested image indexes."
                );
            throw new JsonException("Too many nested image indexes.");
        }

        var digest = inspection.IsPublication
            ? ""
            : await ResolveDigestAsync(reference, cancellationToken);
        var result = await RunAsync(
            ImageToolsArguments(
                inspection.Builder,
                [
                    "inspect",
                    "--raw",
                    inspection.IsPublication ? reference : Repository(reference) + "@" + digest,
                ]
            ),
            inspection.IsPublication ? DockerOperation.Publication : DockerOperation.MetadataRead,
            cancellationToken
        );
        try
        {
            using var document = JsonDocument.Parse(result.StandardOutput);
            var root = document.RootElement;
            if (root.GetProperty("schemaVersion").GetInt32() != 2)
                throw new JsonException("Expected an OCI image manifest or index.");

            if (!root.TryGetProperty("manifests", out var manifests))
            {
                var leaf = inspection.IsPublication
                    ? Descriptor(
                        root,
                        inspection.Kind!,
                        $"Expected a v3 {inspection.Kind} descriptor."
                    )
                    : root.Clone();
                return [new OciManifest(digest, leaf)];
            }

            return await InspectIndexAsync(
                reference,
                manifests,
                inspection,
                depth,
                cancellationToken
            );
        }
        catch (Exception exception)
            when (inspection.IsPublication
                && exception
                    is JsonException
                        or KeyNotFoundException
                        or InvalidOperationException
                        or FormatException
            )
        {
            throw new AboxException(
                $"Registry artifact '{reference}' did not retain a valid v3 {inspection.Kind} descriptor. Publication stopped: {exception.Message}",
                exception
            );
        }
    }

    private async Task<IReadOnlyList<OciManifest>> InspectIndexAsync(
        string reference,
        JsonElement manifests,
        Inspection inspection,
        int depth,
        CancellationToken cancellationToken
    )
    {
        var children = SelectManifests(manifests, inspection);
        if (!inspection.IsPublication)
        {
            var selected = children.ToList();
            if (selected.Count != 1)
                throw new JsonException("No unambiguous runnable platform manifest.");
            children = selected;
        }
        var leaves = new List<OciManifest>();
        foreach (var child in children)
        {
            var childDigest = child.GetProperty("digest").GetString() ?? "";
            if (inspection.IsPublication)
            {
                if (!IsDigest(childDigest))
                    throw new JsonException("Invalid platform manifest digest.");
            }
            else
            {
                childDigest = Digest(childDigest);
            }
            leaves.AddRange(
                await InspectAsync(
                    Repository(reference) + "@" + childDigest,
                    inspection,
                    cancellationToken,
                    depth + 1
                )
            );
        }
        if (inspection.IsPublication && leaves.Count == 0)
            throw new JsonException("No runnable platform manifests.");
        return leaves;
    }

    private async Task<string> ResolveDigestAsync(
        string reference,
        CancellationToken cancellationToken
    )
    {
        if (reference.Contains('@'))
            return Digest(reference[(reference.LastIndexOf('@') + 1)..]);
        var resolved = await RunAsync(
            ["buildx", "imagetools", "inspect", "--format", "{{json .Manifest}}", reference],
            DockerOperation.MetadataRead,
            cancellationToken
        );
        using var identity = JsonDocument.Parse(resolved.StandardOutput);
        return Digest(identity.RootElement.GetProperty("digest").GetString());
    }

    private static IEnumerable<JsonElement> SelectManifests(
        JsonElement manifests,
        Inspection inspection
    )
    {
        foreach (var manifest in manifests.EnumerateArray())
        {
            if (inspection.IsPublication)
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
                yield return manifest;
            }
            else if (
                manifest.TryGetProperty("platform", out var platform)
                && platform.GetProperty("os").GetString() == "linux"
                && platform.GetProperty("architecture").GetString() == inspection.Architecture
            )
            {
                yield return manifest;
            }
        }
    }
}
