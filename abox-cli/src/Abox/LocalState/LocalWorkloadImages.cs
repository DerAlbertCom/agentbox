using System.Text.Json;
using Abox.Infrastructure;
using Abox.Kits;

namespace Abox.LocalState;

/// <summary>Prebuilds workload content in host Docker; sbx only wraps it as a v3 kit at create.</summary>
public sealed class LocalWorkloadImages(
    IHostPrograms programs,
    IOciRegistry registry,
    IConsole console
)
{
    private static readonly OciFailure OperationFailure = new(exitCode =>
        $"Local workload image operation failed (Docker, exit {exitCode}). Check the Docker context and run 'abox build' again. No source-build fallback is performed."
    );

    public async Task<LocalWorkloadImage> BuildAsync(
        string workloadDirectory,
        string runtimeDirectory,
        CancellationToken cancellationToken
    )
    {
        var context = await HostContextAsync(cancellationToken);
        var (reference, imageId) = await BuildImageAsync(
            workloadDirectory,
            context,
            cancellationToken
        );
        WriteRuntimeKit(workloadDirectory, runtimeDirectory, reference);
        console.WriteLine($"Prebuilt workload: {reference} ({imageId}).");
        return new LocalWorkloadImage
        {
            Reference = reference,
            ImageId = imageId,
            DockerContext = context,
            RuntimeKitDirectory = runtimeDirectory,
            RuntimeKitHash = BuildSources.DirectoryHash(runtimeDirectory),
        };
    }

    private async Task<string> HostContextAsync(CancellationToken cancellationToken)
    {
        RequireDocker();
        await registry.RequireBuildxAsync(OperationFailure, cancellationToken);
        var context = (
            await registry.CurrentContextAsync(OperationFailure, cancellationToken)
        ).StandardOutput.Trim();
        if (string.IsNullOrWhiteSpace(context))
        {
            throw new AboxException("Docker did not report its current context.");
        }

        // A context's docker-driver builder shares the image store used by the thin FROM.
        // A selected docker-container/remote builder cannot resolve host-only images.
        await EnsureHostBuilderAsync(context, cancellationToken);
        return context;
    }

    private async Task<(string Reference, string ImageId)> BuildImageAsync(
        string workloadDirectory,
        string context,
        CancellationToken cancellationToken
    )
    {
        var sourceHash = BuildSources.DirectoryHash(workloadDirectory);
        var reference = "docker.io/library/abox-workload:" + Guid.NewGuid().ToString("N");
        console.WriteLine($"Building reusable workload image '{reference}'...");
        await registry.BuildAndLoadAsync(
            new OciLoadBuild(
                workloadDirectory,
                Path.Combine(workloadDirectory, WorkloadKitGenerator.KitName + ".yaml"),
                reference,
                context
            ),
            OperationFailure,
            cancellationToken
        );
        var imageId = await ImageIdAsync(context, reference, cancellationToken);
        if (BuildSources.DirectoryHash(workloadDirectory) != sourceHash)
        {
            throw new AboxException(
                "The workload sources changed during the image build. Run 'abox build' again."
            );
        }

        return (reference, imageId);
    }

    private static void WriteRuntimeKit(
        string workloadDirectory,
        string runtimeDirectory,
        string reference
    )
    {
        Directory.CreateDirectory(runtimeDirectory);
        File.Copy(
            Path.Combine(workloadDirectory, WorkloadKitGenerator.KitName + ".yaml"),
            Path.Combine(runtimeDirectory, WorkloadKitGenerator.KitName + ".yaml")
        );
        File.WriteAllText(
            Path.Combine(runtimeDirectory, WorkloadKitGenerator.KitName + ".dockerfile"),
            $"FROM {reference}\n"
        );
    }

    /// <summary>Fails before sandbox replacement; never rebuilds or pulls a missing local image.</summary>
    public async Task EnsureAvailableAsync(
        LocalWorkloadImage image,
        CancellationToken cancellationToken
    )
    {
        RequireDocker();
        try
        {
            if (BuildSources.DirectoryHash(image.RuntimeKitDirectory) != image.RuntimeKitHash)
            {
                throw new AboxException(
                    "The thin runtime kit changed. Run 'abox build' again; create will not use changed build recipes."
                );
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new AboxException(
                "The thin runtime kit is missing or unreadable. Run 'abox build' again.",
                exception
            );
        }

        await EnsureHostBuilderAsync(image.DockerContext, cancellationToken);
        if (
            await ImageIdAsync(image.DockerContext, image.Reference, cancellationToken)
            != image.ImageId
        )
        {
            throw new AboxException(
                $"The prebuilt workload image '{image.Reference}' was replaced. Run 'abox build' again."
            );
        }
    }

    private async Task EnsureHostBuilderAsync(string context, CancellationToken cancellationToken)
    {
        var builders = (
            await registry.ListBuildersAsync(OperationFailure, cancellationToken)
        ).StandardOutput.Trim();
        var drivers = new HashSet<string>();
        try
        {
            foreach (var line in builders.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                using var document = JsonDocument.Parse(line);
                var builder = document.RootElement;
                if (builder.GetProperty("Name").GetString() == context)
                {
                    drivers.Add(builder.GetProperty("Driver").GetString() ?? "");
                }
            }
        }
        catch (Exception exception)
            when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new AboxException(
                "Cannot read Docker Buildx builders. Local images require a docker-driver builder; run 'abox build' again after fixing Buildx.",
                exception
            );
        }
        if (drivers.Count != 1 || !drivers.Contains("docker"))
        {
            throw new AboxException(
                $"Local workload images require the docker-driver builder for context '{context}', not '{string.Join(", ", drivers)}'. Select a Docker Engine context and run 'abox build' again."
            );
        }
    }

    private async Task<string> ImageIdAsync(
        string context,
        string reference,
        CancellationToken cancellationToken
    )
    {
        var id = (
            await registry.ImageIdAsync(
                new OciLocalImage(context, reference),
                OperationFailure,
                cancellationToken
            )
        ).StandardOutput.Trim();
        if (!OciRegistry.IsDigest(id))
        {
            throw new AboxException(
                $"Docker did not report a valid content ID for '{reference}'. Run 'abox build' again."
            );
        }
        return id;
    }

    private void RequireDocker() =>
        OciRegistry.RequireDocker(
            programs,
            "Local workload images need Docker with Buildx on PATH. Install Docker and run 'abox build' again."
        );
}
