using System.Text.RegularExpressions;
using static Abox.UserSecrets.EndpointSyntax;

namespace Abox.UserSecrets;

internal static partial class PostgresEndpointScanner
{
    internal static EndpointScan ScanField(
        ConnectionEndpointFields fields,
        ConnectionField endpoint,
        HashSet<int> ports
    )
    {
        var postgres = endpoint.Key.Equals("Host", StringComparison.OrdinalIgnoreCase);
        var server = endpoint.Key.Equals("Server", StringComparison.OrdinalIgnoreCase);
        if (!postgres && !server)
            return EndpointScan.Unrecognized;
        var text = fields.Text(endpoint);
        var hosts = text.Split(',');
        var separatePorts = fields.Ports;
        var portParts = separatePorts.SelectMany(field => fields.Text(field).Split(',')).ToArray();
        if (postgres)
        {
            var inline = ScanInline(fields, endpoint, ports);
            if (inline.Kind != EndpointScanKind.Unrecognized)
                return inline;
        }
        var mappedPort =
            separatePorts.Length == 0
                ? postgres && ports.Contains(5432)
                : portParts.Any(part => IsMappedEndpointPort(part, ports));
        if (!hosts.Any(part => IsLoopback(part.Trim())) || !mappedPort)
            return EndpointScan.Unrecognized;
        var singlePort = separatePorts.Length <= 1 && portParts.Length <= 1;
        if (!fields.SingleHost || hosts.Length != 1 || !singlePort)
            return EndpointScan.Unsafe;
        var hostStart = text.Length - text.TrimStart().Length;
        return EndpointScan.Replace(endpoint.Start + hostStart, text.Trim().Length);
    }

    private static EndpointScan ScanInline(
        ConnectionEndpointFields fields,
        ConnectionField endpoint,
        HashSet<int> ports
    )
    {
        var hosts = fields.Text(endpoint).Split(',');
        foreach (var part in hosts)
        {
            var inline = PostgresEndpoint().Match(part);
            if (
                !inline.Success
                || !IsLoopback(inline.Groups["host"].Value)
                || !IsMappedEndpointPort(inline.Groups["port"].Value, ports)
            )
                continue;
            if (!fields.SingleHost || hosts.Length != 1 || fields.Ports.Length != 0)
                return EndpointScan.Unsafe;
            var host = inline.Groups["host"];
            return EndpointScan.Replace(endpoint.Start + host.Index, host.Length);
        }
        return EndpointScan.Unrecognized;
    }

    // libpq query connection options override the authority. Unsupported compound
    // forms stop dispatch even when unchanged; generic URI parsing must not rewrite them.
    internal static EndpointScan ScanUrl(string value, HashSet<int> ports)
    {
        var url = PostgresUrl().Match(value);
        if (!url.Success)
            return EndpointScan.Unrecognized;
        var authority = url.Groups["authority"].Value;
        var hosts = authority[(authority.LastIndexOf('@') + 1)..].Split(',');
        var endpoints = hosts.Select(host => PostgresEndpoint().Match(host)).ToArray();
        var authorityHosts = endpoints
            .Select(
                (endpoint, index) => endpoint.Success ? endpoint.Groups["host"].Value : hosts[index]
            )
            .ToArray();
        var query = ReadQuery(url.Groups["query"].Value);
        string[] Option(string key) =>
            query
                .Where(part => part.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
                .SelectMany(part => part.Value.Split(','))
                .ToArray();
        var queryHosts = Option("host");
        var addresses = Option("hostaddr");
        // host may still be a TLS/authentication name. Empty hostaddr entries fall
        // back positionally; hostless URLs do not gain an assumed default port.
        var queryConnectionHosts =
            addresses.Length == 0
                ? queryHosts
                : addresses
                    .Select(
                        (address, index) =>
                            address.Length > 0
                                ? address
                                : queryHosts.ElementAtOrDefault(index)
                                    ?? authorityHosts.ElementAtOrDefault(index)
                                    ?? ""
                    )
                    .ToArray();
        var queryPorts = Option("port");
        if (hosts.Length == 1 && queryConnectionHosts.Length == 0 && queryPorts.Length == 0)
            return EndpointScan.Unrecognized;
        var connectionHosts =
            queryConnectionHosts.Length > 0 ? queryConnectionHosts : authorityHosts;
        var connectionPorts =
            queryPorts.Length > 0
                ? queryPorts
                : endpoints
                    .Select(endpoint => endpoint.Success ? endpoint.Groups["port"].Value : "")
                    .ToArray();
        return HasMappedLocal(connectionHosts, connectionPorts, ports)
            ? EndpointScan.Unsafe
            : EndpointScan.Unchanged;
    }

    private static bool HasMappedLocal(string[] hosts, string[] endpointPorts, HashSet<int> ports)
    {
        for (var index = 0; index < hosts.Length; index++)
        {
            if (!IsLoopback(hosts[index]))
                continue;
            if (endpointPorts.Length == 1 && IsMappedEndpointPort(endpointPorts[0], ports))
                return true;
            if (
                endpointPorts.Length == hosts.Length
                && IsMappedEndpointPort(endpointPorts[index], ports)
            )
                return true;
            if (
                endpointPorts.Length != 1
                && endpointPorts.Length != hosts.Length
                && endpointPorts.Any(port => IsMappedEndpointPort(port, ports))
            )
                return true;
        }
        return false;
    }

    private static (string Key, string Value)[] ReadQuery(string query) =>
        query
            .Split('&')
            .Select(part => part.Split('=', 2))
            .Where(part => part.Length == 2)
            .Select(part => (Uri.UnescapeDataString(part[0]), Uri.UnescapeDataString(part[1])))
            .ToArray();

    [GeneratedRegex(
        @"\Apostgres(?:ql)?://(?<authority>[^/?#]*)(?:[^?#]*)(?:\?(?<query>[^#]*))?(?:#.*)?\z",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    )]
    private static partial Regex PostgresUrl();
}
