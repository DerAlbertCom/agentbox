using System.Text.Json;
using Abox.Infrastructure;
using Abox.Kits;
using Abox.LocalState;
using Abox.Naming;

namespace Abox.Features.Publish;

internal enum PublicationImageKind
{
    Workload,
    Mixin,
    Set,
}

internal sealed record PublicationPush(
    string Directory,
    string Reference,
    PublicationImageKind Kind,
    string PublicationDirectory
);

/// <summary>Publication's verified writes and all-runnable-platform inspection policy.</summary>
internal sealed class VerifiedPublicationRegistry(
    IOciRegistry registry,
    IConsole console,
    string? builder
)
{
    internal Task<ProcessResult> RequireBuildxAsync(CancellationToken cancellationToken) =>
        RunAsync(["buildx", "version"], false, cancellationToken);

    internal async Task ValidateRegistryKitsAsync(
        IEnumerable<ManifestKit> kits,
        CancellationToken cancellationToken
    )
    {
        foreach (var kit in kits.Where(kit => !kit.IsLocal))
        {
            var descriptors = await InspectAsync(
                new Inspection(kit.Reference, "mixin"),
                cancellationToken
            );
            foreach (var descriptor in descriptors)
                ValidateArguments(descriptor, kit.Arguments);
        }
    }

    internal async Task<string> PushVerifiedAsync(
        PublicationPush request,
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
            arguments.AddRange(["--builder", builder]);
        arguments.AddRange([
            "--file",
            PublicationSources.Descriptor(request.Directory),
            "--tag",
            request.Reference,
            "--metadata-file",
            metadata,
            "--provenance=mode=min",
            "--push",
        ]);
        if (request.Kind == PublicationImageKind.Workload)
            arguments.AddRange(WorkloadKitGenerator.GitHubBuildSecretArguments());
        arguments.Add(request.Directory);
        await RunAsync(arguments, true, cancellationToken);

        var pinned = OciRegistry.Pin(
            request.Reference,
            ReadPushedDigest(metadata, request.Reference)
        );
        await InspectAsync(
            new Inspection(
                pinned,
                request.Kind == PublicationImageKind.Mixin ? "mixin" : "workload"
            ),
            cancellationToken
        );
        return pinned;
    }

    internal async Task PromoteAndConfirmAsync(
        string candidate,
        string requestedReference,
        CancellationToken cancellationToken
    )
    {
        // Only a previously verified immutable candidate reaches this stage. A failure after
        // promotion cannot roll back this tag, nor any earlier squad's successful publication.
        await RunAsync(
            OciRegistry.ImageToolsArguments(
                builder,
                ["create", "--prefer-index=false", "--tag", requestedReference, candidate]
            ),
            true,
            cancellationToken
        );
        await InspectAsync(new Inspection(requestedReference, "workload"), cancellationToken);
    }

    private static string ReadPushedDigest(string metadata, string reference)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(metadata));
            var digest =
                document.RootElement.GetProperty("containerimage.digest").GetString() ?? "";
            if (!OciRegistry.IsDigest(digest))
                throw new JsonException("Missing or invalid image digest.");
            return digest;
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
    }

    private sealed record Inspection(string Reference, string Kind, int Depth = 0);

    private async Task<IReadOnlyList<JsonElement>> InspectAsync(
        Inspection inspection,
        CancellationToken cancellationToken
    )
    {
        var (reference, kind, depth) = inspection;
        if (depth > 4)
            throw new AboxException(
                $"Registry artifact '{reference}' has too many nested image indexes."
            );
        var result = CheckResult(
            await registry.InspectAsync(reference, true, builder, cancellationToken)
        );
        try
        {
            var root = OciRegistry.ParseManifest(result.StandardOutput);
            if (!root.TryGetProperty("manifests", out var manifests))
                return [OciRegistry.Descriptor(root, kind, $"Expected a v3 {kind} descriptor.")];
            return await InspectPlatformsAsync(manifests, inspection, cancellationToken);
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

    private async Task<IReadOnlyList<JsonElement>> InspectPlatformsAsync(
        JsonElement manifests,
        Inspection inspection,
        CancellationToken cancellationToken
    )
    {
        var leaves = new List<JsonElement>();
        foreach (var manifest in manifests.EnumerateArray())
        {
            if (IsAttestation(manifest))
                continue;
            var digest = manifest.GetProperty("digest").GetString() ?? "";
            if (!OciRegistry.IsDigest(digest))
                throw new JsonException("Invalid platform manifest digest.");
            leaves.AddRange(
                await InspectAsync(
                    inspection with
                    {
                        Reference = OciRegistry.Repository(inspection.Reference) + "@" + digest,
                        Depth = inspection.Depth + 1,
                    },
                    cancellationToken
                )
            );
        }
        if (leaves.Count == 0)
            throw new JsonException("No runnable platform manifests.");
        return leaves;
    }

    private static bool IsAttestation(JsonElement manifest) =>
        manifest.TryGetProperty("annotations", out var annotations)
        && annotations.TryGetProperty("vnd.docker.reference.type", out var type)
        && type.GetString() == "attestation-manifest"
        && manifest.TryGetProperty("platform", out var platform)
        && platform.GetProperty("os").GetString() == "unknown"
        && platform.GetProperty("architecture").GetString() == "unknown";

    private static void ValidateArguments(
        JsonElement descriptor,
        IReadOnlyDictionary<string, string> arguments
    )
    {
        foreach (var name in arguments.Keys)
        {
            if (!HasCreatePhaseArgument(descriptor, name))
                throw new AboxException(
                    $"Registry kit argument '{name}' is undeclared or build-phase. Publishing supplied build-phase arguments is not supported."
                );
        }
    }

    private static bool HasCreatePhaseArgument(JsonElement descriptor, string name) =>
        descriptor.TryGetProperty("args", out var declarations)
        && declarations.TryGetProperty(name, out var declaration)
        && !IsBuildPhaseArgument(declaration);

    private static bool IsBuildPhaseArgument(JsonElement declaration) =>
        declaration.TryGetProperty("buildArg", out _)
        || declaration.TryGetProperty("buildSecret", out _);

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
}
