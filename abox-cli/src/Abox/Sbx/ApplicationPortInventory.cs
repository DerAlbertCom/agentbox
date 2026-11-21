using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Abox.Infrastructure;
using Abox.Naming;

namespace Abox.Sbx;

/// <summary>Strict decoding of the runtime's application-port inventory and relay acknowledgments.</summary>
internal static class ApplicationPortInventory
{
    internal static Dictionary<int, int> DecodeRelays(
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
                    || relay.RelayPort < 55536
                    || !TcpPort.IsValid(relay.RelayPort)
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

    internal static List<ApplicationBinding> DecodeBindings(string json)
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
                    || !TcpPort.IsValid(binding.HostPort)
                    || !TcpPort.IsValid(binding.SandboxPort)
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

    private sealed record ApplicationRelay(
        [property: JsonPropertyName("application_port")] int ApplicationPort,
        [property: JsonPropertyName("relay_port")] int RelayPort
    );

    internal sealed record ApplicationBinding(
        [property: JsonPropertyName("host_ip")] string HostIp,
        [property: JsonPropertyName("host_port")] int HostPort,
        [property: JsonPropertyName("sandbox_port")] int SandboxPort,
        [property: JsonPropertyName("protocol")] string Protocol
    );
}
