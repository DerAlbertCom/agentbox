using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Abox.Infrastructure;

namespace Abox.Sbx;

public sealed partial class SbxClient
{
    public async Task EnsureApplicationPortsAsync(
        string sandbox,
        IReadOnlyList<ApplicationPortMapping> mappings,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (
            mappings.Any(mapping =>
                mapping.SandboxPort is < 1 or > 65535 || mapping.HostPort is < 1 or > 65535
            )
            || mappings
                .GroupBy(mapping => mapping.HostPort)
                .Any(group => group.Select(mapping => mapping.SandboxPort).Distinct().Count() > 1)
        )
            throw new AboxException(
                "Application port mappings require ports 1..65535 and one sandbox target per host port. No ports were published."
            );
        if (mappings.Count == 0)
            return;
        // sbx 0.51.0 can race its own startup when publishing to a stopped sandbox.
        // A completed exec waits for startup and restores previously saved bindings first.
        await RunOrThrowAsync(
            new ProcessRequest(ExecutableName, ["exec", sandbox, "true"], StandardInput: ""),
            "sbx exec (start before publishing)",
            cancellationToken
        );
        var existing = await ReadApplicationBindingsAsync(sandbox, cancellationToken);
        foreach (var mapping in mappings)
            if (
                existing.Any(binding =>
                    binding.HostPort == mapping.HostPort
                    && binding.Protocol is "tcp" or "tcp4"
                    && binding.HostIp is "127.0.0.1" or "0.0.0.0"
                    && !Matches(binding, mapping)
                )
            )
                throw new AboxException(
                    $"Host port 127.0.0.1:{mapping.HostPort} has a conflicting runtime mapping. Inspect sandbox ports and free that binding explicitly, then retry; existing mappings were not changed."
                );
        var missing = mappings
            .Distinct()
            .Where(mapping => !existing.Any(binding => Matches(binding, mapping)))
            .ToArray();
        if (missing.Length == 0)
            return;
        var arguments = new List<string> { "ports", sandbox };
        foreach (var mapping in missing)
            arguments.AddRange([
                "--publish",
                $"127.0.0.1:{mapping.HostPort}:{mapping.SandboxPort}/tcp4",
            ]);
        arguments.Add("--json");
        await RunOrThrowAsync(
            new ProcessRequest(ExecutableName, arguments),
            "sbx ports --publish",
            cancellationToken
        );
        var published = await ReadApplicationBindingsAsync(sandbox, cancellationToken);
        if (mappings.Any(mapping => !published.Any(binding => Matches(binding, mapping))))
            throw new AboxException(
                "Runtime did not confirm every requested loopback mapping. Inspect sandbox ports and retry 'abox ports'; some mappings may remain published."
            );
    }

    private async Task<List<ApplicationBinding>> ReadApplicationBindingsAsync(
        string sandbox,
        CancellationToken token
    )
    {
        var result = await RunOrThrowAsync(
            new ProcessRequest(ExecutableName, ["ports", sandbox, "--json"]),
            "sbx ports --json",
            token
        );
        try
        {
            using var document = JsonDocument.Parse(
                result.StandardOutput,
                new JsonDocumentOptions { AllowDuplicateProperties = false }
            );
            var bindings = document.RootElement.Deserialize<List<ApplicationBinding>>();
            if (
                bindings is null
                || bindings.Any(binding =>
                    binding is null
                    || !IPAddress.TryParse(binding.HostIp, out _)
                    || binding.HostPort is < 1 or > 65535
                    || binding.SandboxPort is < 1 or > 65535
                    || binding.Protocol
                        is not ("tcp" or "tcp4" or "tcp6" or "udp" or "udp4" or "udp6")
                )
            )
                throw new JsonException();
            return bindings;
        }
        catch (JsonException)
        {
            throw new AboxException(
                "Cannot read valid local application-port inventory from 'sbx ports --json'. Check runtime compatibility and inspect the sandbox ports before retrying; no mappings were removed."
            );
        }
    }

    private static bool Matches(ApplicationBinding binding, ApplicationPortMapping mapping) =>
        binding.HostIp == "127.0.0.1"
        && binding.Protocol == "tcp4"
        && binding.HostPort == mapping.HostPort
        && binding.SandboxPort == mapping.SandboxPort;

    private sealed record ApplicationBinding(
        [property: JsonPropertyName("host_ip")] string HostIp,
        [property: JsonPropertyName("host_port")] int HostPort,
        [property: JsonPropertyName("sandbox_port")] int SandboxPort,
        [property: JsonPropertyName("protocol")] string Protocol
    );
}
