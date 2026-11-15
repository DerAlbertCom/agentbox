using System.Text;
using System.Text.RegularExpressions;

namespace Abox.UserSecrets;

public sealed partial class UserSecretsCopy
{
    private static bool TryRewriteOracle(
        string value,
        HashSet<int> ports,
        Func<AboxException> unsupported,
        out string target
    )
    {
        target = value;
        var replacements = new List<(int Start, int Length)>();
        var endpoint = CollectOracleHosts(value, ports);
        if (endpoint.Recognized)
        {
            if (endpoint.Hosts.Count > 0 && !endpoint.Valid)
                throw unsupported();
            replacements.AddRange(endpoint.Hosts);
        }
        else
        {
            var fields = ConnectionFields(value, out var uniqueAndComplete);
            var sources = fields
                .Where(field => field.Key.Equals("Data Source", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var recognized = false;
            var allValid = true;
            foreach (var source in sources)
            {
                var sourceHosts = CollectOracleSourceHosts(value, source, ports);
                recognized |= sourceHosts.Recognized;
                allValid &= sourceHosts.Valid;
                replacements.AddRange(sourceHosts.Hosts);
            }
            if (!uniqueAndComplete)
            {
                // The shared field parser intentionally omits an unfinished field. Inspect only
                // that next field's Data Source, never substrings inside credentials or prose.
                var position = fields.Count == 0 ? 0 : fields[^1].Start + fields[^1].Length;
                if (position < value.Length && value[position] is '\'' or '"')
                    position++;
                while (
                    position < value.Length
                    && (char.IsWhiteSpace(value[position]) || value[position] == ';')
                )
                    position++;
                var incomplete = OracleSourcePrefix().Match(value[position..]);
                if (incomplete.Success)
                {
                    position += incomplete.Length;
                    var sourceHosts = CollectOracleSourceHosts(
                        value,
                        new ConnectionField("Data Source", position, value.Length - position),
                        ports
                    );
                    recognized |= sourceHosts.Recognized;
                    replacements.AddRange(sourceHosts.Hosts);
                }
            }
            if (
                replacements.Count > 0
                && (
                    !allValid
                    || !uniqueAndComplete
                    || sources.Length != 1
                    || fields.Count(field => HostFields.Contains(field.Key)) != 1
                    || fields.Any(field =>
                        field.Key.Equals("Port", StringComparison.OrdinalIgnoreCase)
                    )
                )
            )
                throw unsupported();
            if (!recognized)
                return false;
        }
        foreach (var (start, length) in replacements.OrderByDescending(span => span.Start))
            target = ReplaceHost(target, start, length);
        return true;
    }

    private sealed class OracleHostResult(
        bool recognized,
        bool valid,
        IReadOnlyList<(int Start, int Length)> hosts
    )
    {
        public bool Recognized { get; } = recognized;
        public bool Valid { get; } = valid;
        public IReadOnlyList<(int Start, int Length)> Hosts { get; } = hosts;
    }

    private static OracleHostResult CollectOracleSourceHosts(
        string value,
        ConnectionField source,
        HashSet<int> ports
    )
    {
        var quote = source.Start > 0 ? value[source.Start - 1] : '\0';
        if (quote is not ('\'' or '"'))
            return CollectOracleHosts(
                value.Substring(source.Start, source.Length),
                ports,
                source.Start,
                inConnectionString: true
            );

        // Decode the outer connection-string quoting only for parsing. Map host spans
        // back to the untouched source so doubled quotes/options keep their spelling.
        var decoded = new StringBuilder(source.Length);
        var positions = new List<int>();
        for (var index = source.Start; index < source.Start + source.Length; index++)
        {
            positions.Add(index);
            decoded.Append(value[index]);
            if (
                value[index] == quote
                && index + 1 < source.Start + source.Length
                && value[index + 1] == quote
            )
                index++;
        }
        positions.Add(source.Start + source.Length);
        var result = CollectOracleHosts(decoded.ToString(), ports, inConnectionString: true);
        return new OracleHostResult(
            result.Recognized,
            result.Valid,
            result
                .Hosts.Select(host =>
                    (
                        positions[host.Start],
                        positions[host.Start + host.Length] - positions[host.Start]
                    )
                )
                .ToArray()
        );
    }

    private static OracleHostResult CollectOracleHosts(
        string value,
        HashSet<int> ports,
        int offset = 0,
        bool inConnectionString = false
    )
    {
        if (!OracleDescriptorPrefix().IsMatch(value))
            return CollectOracleEasyConnectHosts(value, ports, offset, inConnectionString);
        var replacements = new List<(int Start, int Length)>();
        var parser = new OracleDescriptorParser(value);
        var root = parser.Parse();
        var fullyValid = parser.Complete;
        if (root is not null)
            Visit(root);
        return new OracleHostResult(true, fullyValid, replacements);

        void Visit(OracleDescriptorNode node)
        {
            if (node.Key == "ADDRESS")
            {
                var hosts = node.Children.Where(child => child.Key == "HOST").ToArray();
                var addressPorts = node.Children.Where(child => child.Key == "PORT").ToArray();
                // Missing PORT may use Oracle's default. Do not silently accept a
                // local descriptor when that default is mapped, or guess its protocol semantics.
                var mapped =
                    (addressPorts.Length == 0 && ports.Contains(1521))
                    || addressPorts.Any(child =>
                        IsMappedEndpointPort(value.AsSpan(child.Start, child.Length), ports)
                    );
                var local = hosts
                    .Where(host => IsLoopback(value.Substring(host.Start, host.Length)))
                    .ToArray();
                if (!mapped || local.Length == 0)
                    return;
                var protocols = node.Children.Where(child => child.Key == "PROTOCOL").ToArray();
                var protocol =
                    protocols.Length == 1
                        ? value.Substring(protocols[0].Start, protocols[0].Length)
                        : "";
                fullyValid &=
                    hosts.Length == 1
                    && addressPorts.Length == 1
                    && protocols.Length == 1
                    && (
                        protocol.Equals("tcp", StringComparison.OrdinalIgnoreCase)
                        || protocol.Equals("tcps", StringComparison.OrdinalIgnoreCase)
                    )
                    && !node.Children.Any(child =>
                        child.Key is "HTTPS_PROXY" or "HTTPS_PROXY_PORT"
                    );
                replacements.AddRange(local.Select(host => (offset + host.Start, host.Length)));
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
    }

    private sealed class OracleDescriptorNode(string key)
    {
        public string Key { get; } = key;
        public int Start { get; set; }
        public int Length { get; set; }
        public List<OracleDescriptorNode> Children { get; } = [];
    }

    // A small bounded structural parser keeps original spans, including quoted leaves.
    // Partial trees retain already-known address candidates for fail-closed validation.
    private sealed class OracleDescriptorParser(string value)
    {
        private int position;
        private bool valid = true;
        public bool Complete { get; private set; }

        public OracleDescriptorNode? Parse()
        {
            var root = ReadNode(0);
            Whitespace();
            Complete = valid && position == value.Length;
            return root;
        }

        private OracleDescriptorNode? ReadNode(int depth)
        {
            Whitespace();
            if (depth > 64 || !Take('('))
            {
                valid = false;
                return null;
            }
            Whitespace();
            var keyStart = position;
            while (
                position < value.Length
                && (char.IsAsciiLetterOrDigit(value[position]) || value[position] is '_' or '.')
            )
                position++;
            var key = value[keyStart..position].ToUpperInvariant();
            Whitespace();
            if (key.Length == 0 || !Take('='))
            {
                valid = false;
                return null;
            }
            var node = new OracleDescriptorNode(key);
            Whitespace();
            if (position < value.Length && value[position] == '(')
            {
                while (valid && position < value.Length && value[position] == '(')
                {
                    var child = ReadNode(depth + 1);
                    if (child is not null)
                        node.Children.Add(child);
                    Whitespace();
                }
            }
            else
            {
                var quote =
                    position < value.Length && value[position] is '\'' or '"'
                        ? value[position++]
                        : '\0';
                node.Start = position;
                while (
                    position < value.Length
                    && (quote == '\0' ? value[position] != ')' : value[position] != quote)
                )
                {
                    if (quote == '\0' && value[position] is '(' or '\'' or '"')
                    {
                        valid = false;
                        break;
                    }
                    if (quote != '\0' && value[position] == '\\' && position + 1 < value.Length)
                        position++;
                    position++;
                }
                node.Length = position - node.Start;
                if (quote != '\0')
                    valid &= Take(quote);
                else
                    while (
                        node.Length > 0 && char.IsWhiteSpace(value[node.Start + node.Length - 1])
                    )
                        node.Length--;
            }
            Whitespace();
            valid &= Take(')');
            return node;
        }

        private bool Take(char character)
        {
            if (position == value.Length || value[position] != character)
                return false;
            position++;
            return true;
        }

        private void Whitespace()
        {
            while (position < value.Length && char.IsWhiteSpace(value[position]))
                position++;
        }
    }

    private static OracleHostResult CollectOracleEasyConnectHosts(
        string value,
        HashSet<int> ports,
        int offset,
        bool inConnectionString
    )
    {
        var leading = value.Length - value.TrimStart().Length;
        var text = value.Trim();
        var prefix = OracleEasyPrefix().Match(text);
        if (!prefix.Success && text.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase))
            return new(false, true, []); // SQL Server's tcp:host,port belongs to its dispatcher.
        if (!prefix.Success && text.Contains("://", StringComparison.Ordinal))
            return new(false, true, []);
        var start = prefix.Length;
        var end = text.IndexOfAny(['/', '?'], start);
        if (end < 0)
            end = text.Length;
        var authority = text[start..end];
        // A bare authority followed by whitespace prose is not an endpoint. Explicit
        // Data Source/prefix syntax still opts into malformed-endpoint validation.
        if (!inConnectionString && !prefix.Success && authority.Any(char.IsWhiteSpace))
            return new(false, true, []);
        if (!prefix.Success && end == text.Length && !authority.Contains(':'))
            return new(false, true, []); // A bare name can be a TNS alias.
        if (authority.Contains('=') || authority.Contains('('))
            return new(false, true, []);
        var suffix = text[end..];
        // tcp(s) URLs predate Oracle support. URI-only userinfo/paths/fragments still
        // belong to that dispatcher, rather than becoming malformed Easy Connect.
        if (
            prefix.Success
            && !text.StartsWith("//", StringComparison.Ordinal)
            && !authority.Contains(',')
            && !authority.Contains(';')
            && (authority.Contains('@') || !OracleEasySuffix().IsMatch(suffix))
            && Uri.TryCreate(text, UriKind.Absolute, out var uri)
            && uri.IsWellFormedOriginalString()
        )
            return new(false, true, []);
        var replacements = new List<(int Start, int Length)>();
        var valid =
            !text.Any(char.IsControl)
            && OracleEasySuffix().IsMatch(suffix)
            && OracleEasyOptions(suffix);
        var recognized = false;
        var position = start;
        foreach (var list in authority.Split(';'))
        {
            var pending = new List<(int Start, int Length)>();
            foreach (var address in list.Split(','))
            {
                var unbracketed = OracleUnbracketedIpv6().Match(address);
                if (
                    unbracketed.Success
                    || (!address.StartsWith('[') && address.Contains(':') && IsLoopback(address))
                )
                {
                    recognized = true;
                    valid = false;
                    var ambiguousPort = unbracketed.Success
                        ? unbracketed.Groups["port"].Value
                        : "1521";
                    if (IsMappedEndpointPort(ambiguousPort, ports))
                        replacements.Add((offset + leading + position, address.Length));
                    position += address.Length + 1;
                    continue;
                }
                var match = OracleEasyAddress().Match(address);
                if (!match.Success)
                {
                    valid = false;
                    position += address.Length + 1;
                    continue;
                }
                recognized = true;
                valid &= match.Length == address.Length;
                var host = match.Groups["host"];
                if (IsLoopback(host.Value))
                    pending.Add((offset + leading + position + host.Index, host.Length));
                var port = match.Groups["port"];
                if (port.Success)
                {
                    if (TryParseEndpointPort(port.Value, out var number))
                    {
                        if (ports.Contains(number))
                            replacements.AddRange(pending);
                    }
                    else
                        valid = false;
                    pending.Clear();
                }
                position += address.Length + 1;
            }
            if (ports.Contains(1521))
                replacements.AddRange(pending);
        }
        return new OracleHostResult(recognized, valid, replacements);
    }

    private static bool OracleEasyOptions(string suffix)
    {
        var question = suffix.IndexOf('?');
        if (question < 0)
            return true;
        var position = question + 1;
        while (position < suffix.Length)
        {
            var start = position;
            while (
                position < suffix.Length
                && (char.IsAsciiLetterOrDigit(suffix[position]) || suffix[position] is '_' or '.')
            )
                position++;
            var key = suffix[start..position].ToUpperInvariant();
            if (
                key.Length == 0
                || position == suffix.Length
                || suffix[position++] != '='
                || key
                    is "HOST"
                        or "PORT"
                        or "PROTOCOL"
                        or "ADDRESS"
                        or "ADDRESS_LIST"
                        or "DESCRIPTION"
                        or "DESCRIPTION_LIST"
                        or "HTTPS_PROXY"
                        or "HTTPS_PROXY_PORT"
            )
                return false;
            var quote =
                position < suffix.Length && suffix[position] is '\'' or '"'
                    ? suffix[position++]
                    : '\0';
            var closed = quote == '\0';
            while (position < suffix.Length)
            {
                var character = suffix[position++];
                if (quote != '\0')
                {
                    if (character == '\\' && position < suffix.Length)
                        position++;
                    else if (character == quote)
                    {
                        closed = true;
                        break;
                    }
                }
                else if (character == '&')
                {
                    position--;
                    break;
                }
                else if (
                    char.IsWhiteSpace(character) || character is '\'' or '"' or '(' or ')' or '\\'
                )
                    return false;
            }
            if (!closed)
                return false;
            if (
                position < suffix.Length
                && (suffix[position++] != '&' || position == suffix.Length)
            )
                return false;
        }
        return true;
    }

    [GeneratedRegex(@"\A(?:tcps?:)?//", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OracleEasyPrefix();

    [GeneratedRegex(
        @"\A(?<host>\[[^\]\s]+\]|[a-z0-9._-]+)(?::(?<port>[0-9]+))?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    )]
    private static partial Regex OracleEasyAddress();

    [GeneratedRegex(
        @"\A(?:\:\:1|0:0:0:0:0:0:0:1):(?<port>[0-9]+)\z",
        RegexOptions.CultureInvariant
    )]
    private static partial Regex OracleUnbracketedIpv6();

    [GeneratedRegex(
        @"\A(?:/(?:[a-z0-9._$-]*)(?::(?:dedicated|shared|pooled))?(?:/[a-z0-9._$-]+)?)?(?:\?[^\r\n#]*)?\z",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    )]
    private static partial Regex OracleEasySuffix();

    [GeneratedRegex(
        @"\A\s*\(\s*(?:DESCRIPTION(?:_LIST)?|ADDRESS(?:_LIST)?)\s*=",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    )]
    private static partial Regex OracleDescriptorPrefix();

    [GeneratedRegex(
        @"\AData\s+Source\s*=\s*['"" ]?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    )]
    private static partial Regex OracleSourcePrefix();
}
