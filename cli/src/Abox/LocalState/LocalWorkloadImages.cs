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
    public async Task<LocalWorkloadImage> BuildAsync(
        string workloadDirectory,
        string runtimeDirectory,
        CancellationToken cancellationToken
    )
    {
        RequireDocker();
        await RunAsync(["buildx", "version"], cancellationToken);
        var context = (
            await RunAsync(["context", "show"], cancellationToken)
        ).StandardOutput.Trim();
        if (string.IsNullOrWhiteSpace(context))
        {
            throw new AboxException("Docker did not report its current context.");
        }

        // A context's docker-driver builder shares the image store used by the thin FROM.
        // A selected docker-container/remote builder cannot resolve host-only images.
        await EnsureHostBuilderAsync(context, cancellationToken);
        var sourceHash = BuildSources.DirectoryHash(workloadDirectory);
        var reference = "docker.io/library/abox-workload:" + Guid.NewGuid().ToString("N");
        var arguments = new List<string>
        {
            "--context",
            context,
            "buildx",
            "build",
            "--builder",
            context,
            "--file",
            Path.Combine(workloadDirectory, "workload.yaml"),
            "--tag",
            reference,
            "--provenance=false",
            "--load",
        };
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_TOKEN")))
        {
            arguments.AddRange([
                "--secret",
                $"id={WorkloadKitGenerator.GitHubTokenSecret},env=GITHUB_TOKEN",
            ]);
        }

        arguments.Add(workloadDirectory);
        console.WriteLine($"Building reusable workload image '{reference}'...");
        await RunAsync(arguments, cancellationToken);
        var imageId = await ImageIdAsync(context, reference, cancellationToken);
        if (BuildSources.DirectoryHash(workloadDirectory) != sourceHash)
        {
            throw new AboxException(
                "The workload sources changed during the image build. Run 'abox build' again."
            );
        }

        Directory.CreateDirectory(runtimeDirectory);
        File.Copy(
            Path.Combine(workloadDirectory, "workload.yaml"),
            Path.Combine(runtimeDirectory, "workload.yaml")
        );
        File.WriteAllText(
            Path.Combine(runtimeDirectory, "workload.dockerfile"),
            $"FROM {reference}\n"
        );
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
            await RunAsync(["buildx", "ls", "--format", "json"], cancellationToken)
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
            await RunAsync(
                ["--context", context, "image", "inspect", "--format", "{{.Id}}", reference],
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

    private void RequireDocker()
    {
        if (!programs.Exists("docker"))
        {
            throw new AboxException(
                "Local workload images need Docker with Buildx on PATH. Install Docker and run 'abox build' again."
            );
        }
    }

    private async Task<ProcessResult> RunAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken
    )
    {
        var result = await registry.RunAsync(arguments, false, cancellationToken);
        if (!result.Succeeded)
            throw new AboxException(
                $"Local workload image operation failed (Docker, exit {result.ExitCode}). Check the Docker context and run 'abox build' again. No source-build fallback is performed.{Environment.NewLine}{(result.StandardError + result.StandardOutput).Trim()}"
            );
        return result;
    }
}
