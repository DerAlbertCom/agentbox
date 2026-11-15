using System.Globalization;
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
        if (OracleEndpoint(value, 0, ports, replacements, out var valid))
        {
            if (replacements.Count > 0 && !valid)
                throw unsupported();
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
                recognized |= OracleSourceEndpoint(value, source, ports, replacements, out valid);
                allValid &= valid;
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
                    recognized |= OracleSourceEndpoint(
                        value,
                        new ConnectionField("Data Source", position, value.Length - position),
                        ports,
                        replacements,
                        out _
                    );
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

    private static bool OracleSourceEndpoint(
        string value,
        ConnectionField source,
        HashSet<int> ports,
        List<(int Start, int Length)> replacements,
        out bool valid
    )
    {
        var quote = source.Start > 0 ? value[source.Start - 1] : '\0';
        if (quote is not ('\'' or '"'))
            return OracleEndpoint(
                value.Substring(source.Start, source.Length),
                source.Start,
                ports,
                replacements,
                out valid,
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
        var hosts = new List<(int Start, int Length)>();
        var recognized = OracleEndpoint(
            decoded.ToString(),
            0,
            ports,
            hosts,
            out valid,
            inConnectionString: true
        );
        replacements.AddRange(
            hosts.Select(host =>
                (positions[host.Start], positions[host.Start + host.Length] - positions[host.Start])
            )
        );
        return recognized;
    }

    private static bool OracleEndpoint(
        string value,
        int offset,
        HashSet<int> ports,
        List<(int Start, int Length)> replacements,
        out bool valid,
        bool inConnectionString = false
    )
    {
        if (!OracleDescriptorPrefix().IsMatch(value))
            return OracleEasyConnect(
                value,
                offset,
                ports,
                replacements,
                out valid,
                inConnectionString
            );
        var parser = new OracleDescriptorParser(value);
        var root = parser.Parse();
        var fullyValid = parser.Complete;
        if (root is not null)
            Visit(root);
        valid = fullyValid;
        return true;

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
                        int.TryParse(
                            value.AsSpan(child.Start, child.Length),
                            NumberStyles.None,
                            CultureInfo.InvariantCulture,
                            out var port
                        ) && ports.Contains(port)
                    );
                var local = hosts
                    .Where(host => IsLoopback(value.Substring(host.Start, host.Length)))
                    .ToArray();
                if (!mapped || local.Length == 0)
                    return;
                var protocols = node.Children.Where(child => child.Key == "PROTOCOL").ToArray();
                fullyValid &=
                    hosts.Length == 1
                    && addressPorts.Length == 1
                    && protocols.Length == 1
                    && (
                        value
                            .Substring(protocols[0].Start, protocols[0].Length)
                            .Equals("tcp", StringComparison.OrdinalIgnoreCase)
                        || value
                            .Substring(protocols[0].Start, protocols[0].Length)
                            .Equals("tcps", StringComparison.OrdinalIgnoreCase)
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

    private static bool OracleEasyConnect(
        string value,
        int offset,
        HashSet<int> ports,
        List<(int Start, int Length)> replacements,
        out bool valid,
        bool inConnectionString
    )
    {
        valid = true;
        var leading = value.Length - value.TrimStart().Length;
        var text = value.Trim();
        var prefix = OracleEasyPrefix().Match(text);
        if (!prefix.Success && text.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase))
            return false; // SQL Server's tcp:host,port is handled by the existing dispatcher.
        if (!prefix.Success && text.Contains("://", StringComparison.Ordinal))
            return false;
        var start = prefix.Length;
        var end = text.IndexOfAny(['/', '?'], start);
        if (end < 0)
            end = text.Length;
        var authority = text[start..end];
        // A bare authority followed by whitespace prose is not an endpoint. Explicit
        // Data Source/prefix syntax still opts into malformed-endpoint validation.
        if (!inConnectionString && !prefix.Success && authority.Any(char.IsWhiteSpace))
            return false;
        if (!prefix.Success && end == text.Length && !authority.Contains(':'))
            return false; // A bare name can be a TNS alias, not an Easy Connect endpoint.
        if (authority.Contains('=') || authority.Contains('('))
            return false;
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
            return false;
        valid =
            !text.Any(char.IsControl)
            && OracleEasySuffix().IsMatch(suffix)
            && OracleEasyOptions(suffix);
        var recognized = false;
        var position = start;
        foreach (var list in authority.Split(';'))
        {
            var pending = new List<(int Start, int Length, bool Local)>();
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
                    if (
                        int.TryParse(
                            ambiguousPort,
                            NumberStyles.None,
                            CultureInfo.InvariantCulture,
                            out var mappedNumber
                        ) && ports.Contains(mappedNumber)
                    )
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
                pending.Add(
                    (offset + leading + position + host.Index, host.Length, IsLoopback(host.Value))
                );
                var port = match.Groups["port"];
                if (port.Success)
                {
                    if (
                        int.TryParse(
                            port.Value,
                            NumberStyles.None,
                            CultureInfo.InvariantCulture,
                            out var number
                        ) && number is > 0 and <= 65535
                    )
                    {
                        if (ports.Contains(number))
                            replacements.AddRange(
                                pending
                                    .Where(host => host.Local)
                                    .Select(host => (host.Start, host.Length))
                            );
                    }
                    else
                        valid = false;
                    pending.Clear();
                }
                position += address.Length + 1;
            }
            if (ports.Contains(1521))
                replacements.AddRange(
                    pending.Where(host => host.Local).Select(host => (host.Start, host.Length))
                );
        }
        return recognized;
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
