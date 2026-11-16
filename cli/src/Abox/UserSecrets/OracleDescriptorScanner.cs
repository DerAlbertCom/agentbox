using static Abox.UserSecrets.EndpointSyntax;
using Node = Abox.UserSecrets.OracleDescriptorSyntax.Node;

namespace Abox.UserSecrets;

// Address eligibility is deliberately narrower than descriptor syntax. HOST-like
// leaves under CONNECT_DATA, SECURITY or unknown paths are never destinations.
internal sealed class OracleDescriptorScanner(string value, HashSet<int> ports)
{
    private readonly List<EndpointSourceSpan> hosts = [];
    private bool valid;

    internal static OracleHostScan Scan(string value, HashSet<int> ports)
    {
        var syntax = new OracleDescriptorSyntax(value);
        var root = syntax.Parse();
        var scanner = new OracleDescriptorScanner(value, ports) { valid = syntax.Complete };
        if (root is not null)
            scanner.Visit(root);
        return new(true, scanner.valid, scanner.hosts);
    }

    private void Visit(Node node)
    {
        if (node.Key == "ADDRESS")
        {
            ReadAddress(node);
            return;
        }
        foreach (var child in node.Children)
            if (
                (node.Key, child.Key)
                is
                    ("DESCRIPTION_LIST", "DESCRIPTION")
                    or
                    ("DESCRIPTION", "ADDRESS" or "ADDRESS_LIST")
                    or
                    ("ADDRESS_LIST", "ADDRESS")
            )
                Visit(child);
    }

    private void ReadAddress(Node node)
    {
        var addressHosts = node.Children.Where(child => child.Key == "HOST").ToArray();
        var addressPorts = node.Children.Where(child => child.Key == "PORT").ToArray();
        var local = addressHosts.Where(host => IsLoopback(Text(host))).ToArray();
        if (!IsMapped(addressPorts) || local.Length == 0)
            return;
        valid &= addressHosts.Length == 1 && addressPorts.Length == 1 && SupportsDirectTcp(node);
        hosts.AddRange(local.Select(host => new EndpointSourceSpan(host.Start, host.Length)));
    }

    private bool IsMapped(Node[] addressPorts) =>
        // Missing PORT can imply Oracle's default, but cannot authorize substitution:
        // retain the candidate so a mapped default fails the explicit-port rule.
        (addressPorts.Length == 0 && ports.Contains(1521))
        || addressPorts.Any(port =>
            IsMappedEndpointPort(value.AsSpan(port.Start, port.Length), ports)
        );

    private bool SupportsDirectTcp(Node node)
    {
        var protocols = node.Children.Where(child => child.Key == "PROTOCOL").ToArray();
        if (protocols.Length != 1)
            return false;
        var protocol = Text(protocols[0]);
        return (
                protocol.Equals("tcp", StringComparison.OrdinalIgnoreCase)
                || protocol.Equals("tcps", StringComparison.OrdinalIgnoreCase)
            ) && !node.Children.Any(child => child.Key is "HTTPS_PROXY" or "HTTPS_PROXY_PORT");
    }

    private string Text(Node node) => value.Substring(node.Start, node.Length);
}
