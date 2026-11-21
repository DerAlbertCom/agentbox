using System.Text.Json;
using System.Text.RegularExpressions;

namespace Abox.Infrastructure;

/// <summary>A caller's failure diagnostic and whether Docker output is safe to include.</summary>
public sealed record OciFailure(Func<int, string> Message, bool IncludeOutput = true);

public sealed record OciImage(string Reference, string? Builder = null);

public sealed record OciLocalImage(string Context, string Reference);

public sealed record OciTag(string Candidate, string Reference, string? Builder);

public sealed record OciLoadBuild(
    string Directory,
    string Descriptor,
    string Reference,
    string Context
);

public sealed record OciPushBuild(
    string Directory,
    string Descriptor,
    string Reference,
    string MetadataFile,
    string? Builder,
    bool UseGitHubToken
);

/// <summary>Intent-level Docker operations with caller-specific, value-safe failure diagnostics.</summary>
public interface IOciRegistry
{
    Task RequireBuildxAsync(OciFailure failure, CancellationToken cancellationToken);

    Task BuildAndLoadAsync(
        OciLoadBuild build,
        OciFailure failure,
        CancellationToken cancellationToken
    );

    Task BuildAndPushAsync(
        OciPushBuild build,
        OciFailure failure,
        CancellationToken cancellationToken
    );

    Task<ProcessResult> CurrentContextAsync(
        OciFailure failure,
        CancellationToken cancellationToken
    );

    Task<ProcessResult> ListBuildersAsync(OciFailure failure, CancellationToken cancellationToken);

    Task<ProcessResult> ImageIdAsync(
        OciLocalImage image,
        OciFailure failure,
        CancellationToken cancellationToken
    );

    Task PullAsync(
        string reference,
        string platform,
        OciFailure failure,
        CancellationToken cancellationToken
    );

    Task SaveAsync(
        string reference,
        string archive,
        OciFailure failure,
        CancellationToken cancellationToken
    );

    Task TagAsync(OciTag tag, OciFailure failure, CancellationToken cancellationToken);

    Task<ProcessResult> InspectAsync(
        OciImage image,
        bool raw,
        OciFailure failure,
        CancellationToken cancellationToken
    );
}

/// <summary>Invokes Docker and shares OCI reference and descriptor primitives.</summary>
public sealed class OciRegistry(IProcessRunner runner) : IOciRegistry
{
    /// <summary>The BuildKit secret used by workload recipes and Docker builds.</summary>
    public const string GitHubTokenSecret = "github_token";

    public const string DescriptorAnnotation = "vnd.docker.sandbox.kit.descriptor";
    private static readonly Regex DigestPattern = new(@"\Asha256:[a-f0-9]{64}\z");

    public static void RequireDocker(IHostPrograms programs, string error)
    {
        if (!programs.Exists("docker"))
            throw new AboxException(error);
    }

    public static bool IsDigest(string? value) => value is not null && DigestPattern.IsMatch(value);

    public static string Digest(string? value) =>
        IsDigest(value) ? value! : throw new JsonException("Invalid published digest.");

    public static string Pin(string reference, string digest) =>
        reference[..reference.LastIndexOf(':')] + "@" + digest;

    public static string Repository(string reference)
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

    internal static JsonElement ParseManifest(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 2)
            throw new JsonException("Expected an OCI image manifest or index.");
        return root.Clone();
    }

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

    private async Task<ProcessResult> RunAsync(
        IReadOnlyList<string> arguments,
        bool attachConsole,
        OciFailure failure,
        CancellationToken cancellationToken
    )
    {
        var result = await runner.RunAsync(
            new ProcessRequest("docker", arguments, AttachConsole: attachConsole),
            cancellationToken
        );
        if (!result.Succeeded)
            throw new AboxException(
                failure.Message(result.ExitCode)
                    + (
                        failure.IncludeOutput
                            ? Environment.NewLine
                                + (result.StandardError + result.StandardOutput).Trim()
                            : ""
                    )
            );
        return result;
    }

    public async Task RequireBuildxAsync(OciFailure failure, CancellationToken cancellationToken) =>
        await RunAsync(["buildx", "version"], false, failure, cancellationToken);

    public async Task BuildAndLoadAsync(
        OciLoadBuild build,
        OciFailure failure,
        CancellationToken cancellationToken
    )
    {
        var arguments = new List<string>
        {
            "--context",
            build.Context,
            "buildx",
            "build",
            "--builder",
            build.Context,
            "--file",
            build.Descriptor,
            "--tag",
            build.Reference,
            "--provenance=false",
            "--load",
        };
        arguments.AddRange(GitHubBuildSecretArguments());
        arguments.Add(build.Directory);
        await RunAsync(arguments, false, failure, cancellationToken);
    }

    public async Task BuildAndPushAsync(
        OciPushBuild build,
        OciFailure failure,
        CancellationToken cancellationToken
    )
    {
        var arguments = new List<string> { "buildx", "build" };
        if (build.Builder is not null)
            arguments.AddRange(["--builder", build.Builder]);
        arguments.AddRange([
            "--file",
            build.Descriptor,
            "--tag",
            build.Reference,
            "--metadata-file",
            build.MetadataFile,
            "--provenance=mode=min",
            "--push",
        ]);
        if (build.UseGitHubToken)
            arguments.AddRange(GitHubBuildSecretArguments());
        arguments.Add(build.Directory);
        await RunAsync(arguments, true, failure, cancellationToken);
    }

    private static IReadOnlyList<string> GitHubBuildSecretArguments() =>
        string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_TOKEN"))
            ? []
            : ["--secret", $"id={GitHubTokenSecret},env=GITHUB_TOKEN"];

    public Task<ProcessResult> CurrentContextAsync(
        OciFailure failure,
        CancellationToken cancellationToken
    ) => RunAsync(["context", "show"], false, failure, cancellationToken);

    public Task<ProcessResult> ListBuildersAsync(
        OciFailure failure,
        CancellationToken cancellationToken
    ) => RunAsync(["buildx", "ls", "--format", "json"], false, failure, cancellationToken);

    public Task<ProcessResult> ImageIdAsync(
        OciLocalImage image,
        OciFailure failure,
        CancellationToken cancellationToken
    ) =>
        RunAsync(
            [
                "--context",
                image.Context,
                "image",
                "inspect",
                "--format",
                "{{.Id}}",
                image.Reference,
            ],
            false,
            failure,
            cancellationToken
        );

    public async Task PullAsync(
        string reference,
        string platform,
        OciFailure failure,
        CancellationToken cancellationToken
    ) =>
        await RunAsync(
            ["pull", "--platform", platform, reference],
            false,
            failure,
            cancellationToken
        );

    public async Task SaveAsync(
        string reference,
        string archive,
        OciFailure failure,
        CancellationToken cancellationToken
    ) =>
        await RunAsync(
            ["image", "save", "--output", archive, reference],
            false,
            failure,
            cancellationToken
        );

    public async Task TagAsync(
        OciTag tag,
        OciFailure failure,
        CancellationToken cancellationToken
    ) =>
        await RunAsync(
            ImageToolsArguments(
                tag.Builder,
                ["create", "--prefer-index=false", "--tag", tag.Reference, tag.Candidate]
            ),
            true,
            failure,
            cancellationToken
        );

    public Task<ProcessResult> InspectAsync(
        OciImage image,
        bool raw,
        OciFailure failure,
        CancellationToken cancellationToken
    ) =>
        RunAsync(
            ImageToolsArguments(
                image.Builder,
                raw
                    ? ["inspect", "--raw", image.Reference]
                    : ["inspect", "--format", "{{json .Manifest}}", image.Reference]
            ),
            false,
            failure,
            cancellationToken
        );
}
