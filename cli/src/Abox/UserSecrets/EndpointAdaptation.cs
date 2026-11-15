using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace Abox.UserSecrets;

// Pure adaptation of resolved settings. The caller supplies only authorized host ports
// and value-free setting context; selection, policy discovery and copying stay outside.
internal static partial class EndpointAdaptation
{
    private static string RewriteEndpoint(string value, HashSet<int> ports, string setting)
    {
        AboxException Unsupported() =>
            new(
                $"{setting}: recognized mapped-local endpoint has an unsupported or ambiguous format. Use a well-formed supported URL, database connection string, or Oracle Easy Connect/descriptor endpoint; no stores were copied."
            );
        if (TryRewriteOracle(value, ports, Unsupported, out var oracle))
            return oracle;
        if (IsMultiHostOrQueryPostgresUrl(value, ports, out var hasMappedLocalEndpoint))
        {
            if (hasMappedLocalEndpoint)
                throw Unsupported();
            return value;
        }
        var match = UrlEndpoint().Match(value);
        if (match.Success)
        {
            var host = match.Groups["host"];
            if (!IsLoopback(host.Value))
                return value;
            var parsed = Uri.TryCreate(value, UriKind.Absolute, out var uri);
            int port;
            if (match.Groups["port"].Success)
            {
                if (!TryParseEndpointPort(match.Groups["port"].Value, out port))
                    return value;
            }
            else if (Uri.TryCreate(value[..match.Length], UriKind.Absolute, out var authority))
                port = authority.Port;
            else
                return value;
            if (!ports.Contains(port))
                return value;
            if (
                value.Any(character =>
                    char.IsWhiteSpace(character) || char.IsControl(character) || character == '\\'
                )
                || !parsed
                || !uri!.IsWellFormedOriginalString()
            )
                throw Unsupported();
            return ReplaceHost(value, host.Index, host.Length);
        }
        var fields = ConnectionFields(value, out var fullyParsedAndUnique);
        var hosts = fields.Where(field => HostFields.Contains(field.Key)).ToArray();
        var separatePorts = fields
            .Where(field => field.Key.Equals("Port", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var hasSingleUnambiguousHost = fullyParsedAndUnique && hosts.Length == 1;
        string Text(ConnectionField field) => value.Substring(field.Start, field.Length);
        foreach (var endpoint in hosts)
        {
            var text = Text(endpoint);
            var sql = SqlEndpoint().Match(text);
            if (sql.Success && IsMappedEndpointPort(sql.Groups["port"].Value, ports))
            {
                if (
                    !hasSingleUnambiguousHost
                    || separatePorts.Length != 0
                    || endpoint.Key.Equals("Host", StringComparison.OrdinalIgnoreCase)
                )
                    throw Unsupported();
                return ReplaceHost(
                    value,
                    endpoint.Start + sql.Groups["host"].Index,
                    sql.Groups["host"].Length
                );
            }
            var hostParts = text.Split(',');
            var portParts = separatePorts.SelectMany(field => Text(field).Split(',')).ToArray();
            var postgres = endpoint.Key.Equals("Host", StringComparison.OrdinalIgnoreCase);
            var server = endpoint.Key.Equals("Server", StringComparison.OrdinalIgnoreCase);
            if (!postgres && !server)
                continue;
            if (postgres)
            {
                foreach (var part in hostParts)
                {
                    var inline = PostgresEndpoint().Match(part);
                    if (
                        !inline.Success
                        || !IsLoopback(inline.Groups["host"].Value)
                        || !IsMappedEndpointPort(inline.Groups["port"].Value, ports)
                    )
                        continue;
                    if (
                        !hasSingleUnambiguousHost
                        || hostParts.Length != 1
                        || separatePorts.Length != 0
                    )
                        throw Unsupported();
                    return ReplaceHost(
                        value,
                        endpoint.Start + inline.Groups["host"].Index,
                        inline.Groups["host"].Length
                    );
                }
            }
            var mappedPort =
                separatePorts.Length == 0
                    ? postgres && ports.Contains(5432)
                    : portParts.Any(part => IsMappedEndpointPort(part, ports));
            if (!hostParts.Any(part => IsLoopback(part.Trim())) || !mappedPort)
                continue;
            if (
                !hasSingleUnambiguousHost
                || hostParts.Length != 1
                || separatePorts.Length > 1
                || portParts.Length > 1
            )
                throw Unsupported();
            var hostStart = text.Length - text.TrimStart().Length;
            return ReplaceHost(value, endpoint.Start + hostStart, text.Trim().Length);
        }
        return value;
    }

    private static bool IsMultiHostOrQueryPostgresUrl(
        string value,
        HashSet<int> ports,
        out bool hasMappedLocalEndpoint
    )
    {
        hasMappedLocalEndpoint = false;
        var url = PostgresUrl().Match(value);
        if (!url.Success)
            return false;
        var authority = url.Groups["authority"].Value;
        var hosts = authority[(authority.LastIndexOf('@') + 1)..].Split(',');
        var endpoints = hosts.Select(host => PostgresEndpoint().Match(host)).ToArray();
        var authorityHosts = endpoints
            .Select(
                (endpoint, index) => endpoint.Success ? endpoint.Groups["host"].Value : hosts[index]
            )
            .ToArray();
        var query = url.Groups["query"]
            .Value.Split('&')
            .Select(part => part.Split('=', 2))
            .Where(part => part.Length == 2)
            .Select(part =>
                (Key: Uri.UnescapeDataString(part[0]), Value: Uri.UnescapeDataString(part[1]))
            )
            .ToArray();
        var queryHosts = query
            .Where(part => part.Key.Equals("host", StringComparison.OrdinalIgnoreCase))
            .SelectMany(part => part.Value.Split(','))
            .ToArray();
        var queryHostAddresses = query
            .Where(part => part.Key.Equals("hostaddr", StringComparison.OrdinalIgnoreCase))
            .SelectMany(part => part.Value.Split(','))
            .ToArray();
        // libpq connects to hostaddr when supplied; host can still name TLS/authentication.
        // Empty hostaddr entries fall back to their corresponding host, without a default port.
        var queryConnectionHosts =
            queryHostAddresses.Length == 0
                ? queryHosts
                : queryHostAddresses
                    .Select(
                        (address, index) =>
                            address.Length > 0
                                ? address
                                : queryHosts.ElementAtOrDefault(index)
                                    ?? authorityHosts.ElementAtOrDefault(index)
                                    ?? ""
                    )
                    .ToArray();
        var queryPorts = query
            .Where(part => part.Key.Equals("port", StringComparison.OrdinalIgnoreCase))
            .SelectMany(part => part.Value.Split(','))
            .ToArray();
        if (hosts.Length == 1 && queryConnectionHosts.Length == 0 && queryPorts.Length == 0)
            return false;
        bool IsMappedPort(string text) => IsMappedEndpointPort(text, ports);
        // Query connection-host/port options override the authority. Match positional lists
        // before testing locality, so a remote host's mapped port cannot implicate a local host.
        var connectionHosts =
            queryConnectionHosts.Length > 0 ? queryConnectionHosts : authorityHosts;
        var connectionPorts =
            queryPorts.Length > 0
                ? queryPorts
                : endpoints
                    .Select(endpoint => endpoint.Success ? endpoint.Groups["port"].Value : "")
                    .ToArray();
        for (var index = 0; index < connectionHosts.Length; index++)
        {
            if (!IsLoopback(connectionHosts[index]))
                continue;
            if (connectionPorts.Length == 1)
                hasMappedLocalEndpoint = IsMappedPort(connectionPorts[0]);
            else if (connectionPorts.Length == connectionHosts.Length)
                hasMappedLocalEndpoint = IsMappedPort(connectionPorts[index]);
            else
                hasMappedLocalEndpoint = connectionPorts.Any(IsMappedPort);
            if (hasMappedLocalEndpoint)
                break;
        }
        // Hostless PostgreSQL URLs have no assumed default port in this bounded model.
        return true;
    }

    private static bool TryParseEndpointPort(ReadOnlySpan<char> text, out int port) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out port)
        && port is >= 1 and <= 65535;

    private static bool IsMappedEndpointPort(ReadOnlySpan<char> text, HashSet<int> ports) =>
        TryParseEndpointPort(text, out var port) && ports.Contains(port);

    private static string ReplaceHost(string value, int start, int length) =>
        string.Concat(value.AsSpan(0, start), "host.docker.internal", value.AsSpan(start + length));

    private static bool IsLoopback(string host)
    {
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            return true;
        var text = host.Trim('[', ']');
        return IPAddress.TryParse(text, out var address)
            && IPAddress.IsLoopback(address)
            && (
                address.AddressFamily == AddressFamily.InterNetworkV6
                || text.Count(character => character == '.') == 3
            );
    }

    private static readonly HashSet<string> HostFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "Server",
        "Data Source",
        "Address",
        "Addr",
        "Network Address",
        "Host",
    };

    private sealed record ConnectionField(string Key, int Start, int Length);

    // Keep source spans rather than rebuilding connection strings (quoting/order/credentials matter).
    private static List<ConnectionField> ConnectionFields(
        string value,
        out bool fullyParsedAndUnique
    )
    {
        fullyParsedAndUnique = false;
        var fields = new List<ConnectionField>();
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var duplicate = false;
        var position = 0;
        while (position < value.Length)
        {
            while (
                position < value.Length
                && (char.IsWhiteSpace(value[position]) || value[position] == ';')
            )
                position++;
            if (position == value.Length)
                break;
            var keyStart = position;
            while (position < value.Length && value[position] != '=')
            {
                var character = value[position++];
                if (!(char.IsAsciiLetterOrDigit(character) || character is ' ' or '_' or '-'))
                    return fields;
            }
            if (position == value.Length)
                return fields;
            var key = value[keyStart..position].Trim();
            if (key.Length == 0)
                return fields;
            duplicate |= !keys.Add(key);
            position++;
            while (position < value.Length && char.IsWhiteSpace(value[position]))
                position++;
            var start = position;
            int end;
            if (position < value.Length && value[position] is '\'' or '"')
            {
                var quote = value[position++];
                start = position;
                while (position < value.Length)
                {
                    if (value[position] != quote)
                    {
                        position++;
                        continue;
                    }
                    if (position + 1 < value.Length && value[position + 1] == quote)
                    {
                        position += 2;
                        continue;
                    }
                    break;
                }
                if (position == value.Length)
                    return fields;
                end = position++;
                while (position < value.Length && char.IsWhiteSpace(value[position]))
                    position++;
                if (position < value.Length && value[position] != ';')
                    return fields;
            }
            else
            {
                while (position < value.Length && value[position] != ';')
                {
                    if (value[position] is '\'' or '"' or '{' or '}')
                        return fields;
                    position++;
                }
                end = position;
                while (end > start && char.IsWhiteSpace(value[end - 1]))
                    end--;
            }
            fields.Add(new ConnectionField(key, start, end - start));
        }
        fullyParsedAndUnique = !duplicate;
        return fields;
    }

    [GeneratedRegex(
        @"\A[a-z][a-z0-9+.-]*://(?:[^\s/@?#]*@)?(?<host>\[[^\]]+\]|[^\s:/?#\\]+)(?::(?<port>[0-9]+))?(?=[/?#\\\s]|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    )]
    private static partial Regex UrlEndpoint();

    [GeneratedRegex(
        @"\Apostgres(?:ql)?://(?<authority>[^/?#]*)(?:[^?#]*)(?:\?(?<query>[^#]*))?(?:#.*)?\z",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    )]
    private static partial Regex PostgresUrl();

    [GeneratedRegex(
        @"\A(?<host>\[[^\]]+\]|[^:,]+):(?<port>[0-9]+)\z",
        RegexOptions.CultureInvariant
    )]
    private static partial Regex PostgresEndpoint();

    [GeneratedRegex(
        @"\A(?:tcp:)?(?<host>localhost|127\.0\.0\.1),(?<port>[0-9]{1,5})\z",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    )]
    private static partial Regex SqlEndpoint();
}
