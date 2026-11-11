using System.Text.Json;
using System.Text.RegularExpressions;

namespace Abox.Infrastructure;

/// <summary>Raw Docker operations and parameterised OCI inspection, without caller-specific failure policy.</summary>
public interface IOciRegistry
{
    Task<ProcessResult> RunAsync(
        IReadOnlyList<string> arguments,
        bool attachConsole,
        CancellationToken cancellationToken
    );

    Task<ProcessResult> InspectAsync(
        string reference,
        bool raw,
        string? builder,
        CancellationToken cancellationToken
    );
}

/// <summary>Invokes Docker and shares OCI reference and descriptor primitives.</summary>
public sealed class OciRegistry(IProcessRunner runner) : IOciRegistry
{
    public const string DescriptorAnnotation = "vnd.docker.sandbox.kit.descriptor";
    private static readonly Regex DigestPattern = new(@"\Asha256:[a-f0-9]{64}\z");

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

    public Task<ProcessResult> RunAsync(
        IReadOnlyList<string> arguments,
        bool attachConsole,
        CancellationToken cancellationToken
    ) =>
        runner.RunAsync(
            new ProcessRequest("docker", arguments, AttachConsole: attachConsole),
            cancellationToken
        );

    public Task<ProcessResult> InspectAsync(
        string reference,
        bool raw,
        string? builder,
        CancellationToken cancellationToken
    ) =>
        RunAsync(
            ImageToolsArguments(
                builder,
                raw
                    ? ["inspect", "--raw", reference]
                    : ["inspect", "--format", "{{json .Manifest}}", reference]
            ),
            false,
            cancellationToken
        );
}
