using System.Text.RegularExpressions;
using static Abox.UserSecrets.EndpointSyntax;

namespace Abox.UserSecrets;

internal sealed partial class OracleEasyConnectScanner(string value, HashSet<int> ports)
{
    private readonly string text = value.Trim();
    private readonly int leading = value.Length - value.TrimStart().Length;
    private readonly List<EndpointSourceSpan> hosts = [];
    private int position;
    private bool valid;
    private bool recognized;

    internal static OracleHostScan Scan(
        string value,
        HashSet<int> ports,
        bool inConnectionString
    ) => new OracleEasyConnectScanner(value, ports).Read(inConnectionString);

    private OracleHostScan Read(bool inConnectionString)
    {
        var prefix = OracleEasyPrefix().Match(text);
        var end = text.IndexOfAny(['/', '?'], prefix.Length);
        if (end < 0)
            end = text.Length;
        var authority = text[prefix.Length..end];
        var suffix = text[end..];
        if (
            !Recognizes(prefix, authority, inConnectionString)
            || IsGenericUri(prefix, authority, suffix)
        )
            return new(false, true, []);
        valid =
            !text.Any(char.IsControl)
            && OracleEasySuffix().IsMatch(suffix)
            && OracleEasyConnectOptions.Valid(suffix);
        position = prefix.Length;
        foreach (var list in authority.Split(';'))
            ReadList(list);
        return new(recognized, valid, hosts);
    }

    private bool Recognizes(Match prefix, string authority, bool inConnectionString)
    {
        if (!prefix.Success)
        {
            // SQL Server's tcp:host,port and other schemes retain their dispatchers.
            if (
                text.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase)
                || text.Contains("://", StringComparison.Ordinal)
            )
                return false;
            if (!inConnectionString && authority.Any(char.IsWhiteSpace))
                return false;
            if (authority.Length == text.Length && !authority.Contains(':'))
                return false; // A bare name can be a TNS alias.
        }
        return !authority.Contains('=') && !authority.Contains('(');
    }

    private bool IsGenericUri(Match prefix, string authority, string suffix) =>
        // URI-only userinfo, paths and fragments predate Oracle support.
        prefix.Success
        && !text.StartsWith("//", StringComparison.Ordinal)
        && !authority.Contains(',')
        && !authority.Contains(';')
        && (authority.Contains('@') || !OracleEasySuffix().IsMatch(suffix))
        && Uri.TryCreate(text, UriKind.Absolute, out var uri)
        && uri.IsWellFormedOriginalString();

    private void ReadList(string list)
    {
        var pending = new List<EndpointSourceSpan>();
        foreach (var address in list.Split(','))
        {
            ReadAddress(address, pending);
            position += address.Length + 1;
        }
        if (ports.Contains(1521))
            hosts.AddRange(pending);
    }

    private void ReadAddress(string address, List<EndpointSourceSpan> pending)
    {
        if (ReadAmbiguousIpv6(address))
            return;
        var match = OracleEasyAddress().Match(address);
        if (!match.Success)
        {
            valid = false;
            return;
        }
        recognized = true;
        valid &= match.Length == address.Length;
        var host = match.Groups["host"];
        if (IsLoopback(host.Value))
            pending.Add(new(leading + position + host.Index, host.Length));
        var port = match.Groups["port"];
        if (!port.Success)
            return;
        if (TryParseEndpointPort(port.Value, out var number))
        {
            if (ports.Contains(number))
                hosts.AddRange(pending);
        }
        else
            valid = false;
        pending.Clear();
    }

    private bool ReadAmbiguousIpv6(string address)
    {
        var match = OracleUnbracketedIpv6().Match(address);
        var bareLoopback = !address.StartsWith('[') && address.Contains(':') && IsLoopback(address);
        if (!match.Success && !bareLoopback)
            return false;
        recognized = true;
        valid = false;
        var port = match.Success ? match.Groups["port"].Value : "1521";
        if (IsMappedEndpointPort(port, ports))
            hosts.Add(new(leading + position, address.Length));
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
}
