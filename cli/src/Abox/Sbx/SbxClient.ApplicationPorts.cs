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
        ValidateApplicationMappings(mappings);
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
        var relayPorts = await EnsureApplicationRelaysAsync(
            sandbox,
            mappings,
            existing,
            cancellationToken
        );
        var forwarded = mappings
            .Select(mapping => new ApplicationPortMapping(
                relayPorts[mapping.SandboxPort],
                mapping.HostPort
            ))
            .Distinct()
            .ToArray();
        RejectApplicationBindingConflicts(existing, forwarded);
        var missing = forwarded
            .Where(mapping => !existing.Any(binding => Matches(binding, mapping)))
            .ToArray();
        if (missing.Length == 0)
            return;
        await PublishMissingApplicationBindingsAsync(sandbox, missing, cancellationToken);
        await ConfirmApplicationBindingsAsync(sandbox, forwarded, cancellationToken);
    }

    private static void ValidateApplicationMappings(IReadOnlyList<ApplicationPortMapping> mappings)
    {
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
        if (
            mappings.Select(mapping => mapping.HostPort - mapping.SandboxPort).Distinct().Count()
            != 1
        )
            throw new AboxException(
                "Application port mappings must use the same offset. No runtime changes were attempted."
            );
    }

    private static void RejectApplicationBindingConflicts(
        IReadOnlyList<ApplicationBinding> existing,
        IReadOnlyList<ApplicationPortMapping> forwarded
    )
    {
        foreach (var mapping in forwarded)
        {
            var conflict = existing.FirstOrDefault(binding =>
                binding.HostPort == mapping.HostPort
                && binding.Protocol is "tcp" or "tcp4"
                && binding.HostIp is "127.0.0.1" or "0.0.0.0"
                && !Matches(binding, mapping)
            );
            if (conflict is not null)
                throw new AboxException(
                    $"Host port 127.0.0.1:{mapping.HostPort} has a conflicting runtime mapping {conflict.HostIp}:{conflict.HostPort}:{conflict.SandboxPort}/{conflict.Protocol}. Ownership is unproven; abox will not replace it. Inspect sandbox ports and, only if you intend to replace it, explicitly remove only that exact binding before retrying; existing mappings were not changed."
                );
        }
    }

    private async Task PublishMissingApplicationBindingsAsync(
        string sandbox,
        IReadOnlyList<ApplicationPortMapping> missing,
        CancellationToken cancellationToken
    )
    {
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
    }

    private async Task ConfirmApplicationBindingsAsync(
        string sandbox,
        IReadOnlyList<ApplicationPortMapping> forwarded,
        CancellationToken cancellationToken
    )
    {
        var published = await ReadApplicationBindingsAsync(sandbox, cancellationToken);
        if (forwarded.Any(mapping => !published.Any(binding => Matches(binding, mapping))))
            throw new AboxException(
                "Runtime did not confirm every requested loopback mapping. Inspect sandbox ports and retry 'abox ports'; some mappings may remain published."
            );
    }

    private async Task<Dictionary<int, int>> EnsureApplicationRelaysAsync(
        string sandbox,
        IReadOnlyList<ApplicationPortMapping> mappings,
        IReadOnlyList<ApplicationBinding> existing,
        CancellationToken token
    )
    {
        using var source = new StreamReader(
            typeof(SbxClient).Assembly.GetManifestResourceStream("Abox.ApplicationPortsRelay")!
        );
        var result = await RunOrThrowAsync(
            new ProcessRequest(
                ExecutableName,
                ApplicationRelayArguments(sandbox, mappings, existing),
                StandardInput: await source.ReadToEndAsync(token),
                UseUtf8StandardInput: true
            ),
            "application-port relay (requires Python 3.8+ in the sandbox; nothing is installed automatically)",
            token
        );
        return DecodeApplicationRelays(result.StandardOutput, mappings);
    }

    private static List<string> ApplicationRelayArguments(
        string sandbox,
        IReadOnlyList<ApplicationPortMapping> mappings,
        IReadOnlyList<ApplicationBinding> existing
    )
    {
        var arguments = new List<string>
        {
            "exec",
            "-i",
            sandbox,
            "python3",
            "-I",
            "-c",
            "import sys; SOURCE=sys.stdin.read(); exec(compile(SOURCE, '<abox-application-ports>', 'exec'))",
        };
        arguments.AddRange(
            mappings
                .Select(mapping => mapping.SandboxPort)
                .Distinct()
                .Order()
                .Select(port => port.ToString(System.Globalization.CultureInfo.InvariantCulture))
        );
        arguments.Add("--reserved");
        arguments.AddRange(
            existing
                .Select(binding => binding.SandboxPort)
                .Distinct()
                .Order()
                .Select(port => port.ToString(System.Globalization.CultureInfo.InvariantCulture))
        );
        arguments.AddRange([
            "--offset",
            (mappings[0].HostPort - mappings[0].SandboxPort).ToString(
                System.Globalization.CultureInfo.InvariantCulture
            ),
            "--bindings",
            JsonSerializer.Serialize(
                existing
                    .Where(binding => binding.Protocol is "tcp" or "tcp4" or "tcp6")
                    .Select(binding => new[] { binding.SandboxPort, binding.HostPort })
            ),
        ]);
        return arguments;
    }

    private static Dictionary<int, int> DecodeApplicationRelays(
        string json,
        IReadOnlyList<ApplicationPortMapping> mappings
    )
    {
        try
        {
            using var document = JsonDocument.Parse(
                json,
                new JsonDocumentOptions { AllowDuplicateProperties = false }
            );
            var relays =
                document.RootElement.Deserialize<List<ApplicationRelay>>()
                ?? throw new JsonException();
            var requested = mappings
                .Select(mapping => mapping.SandboxPort)
                .Distinct()
                .Order()
                .ToArray();
            if (
                relays.Any(relay =>
                    relay is null
                    || relay.RelayPort is < 55536 or > 65535
                    || requested.Contains(relay.RelayPort)
                )
                || !relays.Select(relay => relay.ApplicationPort).Order().SequenceEqual(requested)
                || relays.Select(relay => relay.RelayPort).Distinct().Count() != relays.Count
            )
                throw new JsonException();
            return relays.ToDictionary(relay => relay.ApplicationPort, relay => relay.RelayPort);
        }
        catch (JsonException)
        {
            throw new AboxException(
                "Application-port relay did not confirm every requested listener. Check sandbox Python 3.8+ and relay state, then retry; some relays may remain running."
            );
        }
    }

    private sealed record ApplicationRelay(
        [property: JsonPropertyName("application_port")] int ApplicationPort,
        [property: JsonPropertyName("relay_port")] int RelayPort
    );

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
        return DecodeApplicationBindings(result.StandardOutput);
    }

    private static List<ApplicationBinding> DecodeApplicationBindings(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(
                json,
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
                "Cannot read valid local application-port inventory from 'sbx ports --json'. Check runtime compatibility and inspect sandbox ports before retrying; no further changes were attempted after this inventory failure."
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
