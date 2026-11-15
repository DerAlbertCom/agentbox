using System.Net;
using System.Net.Sockets;

namespace Abox.UserSecrets;

internal static partial class EndpointAdaptation
{
    internal const string DatabaseHostKey = "Database_Config:Host";
    internal const string DatabasePortKey = "Database_Config:Port";

    internal static string Rewrite(
        string value,
        HashSet<int> ports,
        string key,
        IReadOnlyDictionary<string, string?> effective,
        string setting
    )
    {
        if (key.Equals(DatabasePortKey, StringComparison.OrdinalIgnoreCase))
            return value;
        if (!key.Equals(DatabaseHostKey, StringComparison.OrdinalIgnoreCase))
            return RewriteEndpoint(value, ports, setting);
        if (!effective.TryGetValue(DatabasePortKey, out var portText) || portText is null)
            return value;
        if (IsSplitLoopback(value) && IsMappedEndpointPort(portText, ports))
            return "host.docker.internal";
        if (
            portText.Split(',').Any(part => IsMappedEndpointPort(part.Trim(), ports))
            && HasSplitLoopback(value)
        )
            throw new AboxException(
                $"{setting}: recognized mapped-local split setting has an unsupported or ambiguous format. Use one literal loopback Host and one decimal Port in Database_Config; no stores were copied."
            );
        return value;
    }

    private static bool IsSplitLoopback(string host)
    {
        if (host.Contains('[') || host.Contains(']'))
            return host.StartsWith('[')
                && host.EndsWith(']')
                && IPAddress.TryParse(host[1..^1], out var address)
                && address.AddressFamily == AddressFamily.InterNetworkV6
                && IPAddress.IsLoopback(address);
        return IsLoopback(host);
    }

    private static bool HasSplitLoopback(string host) =>
        host.Split(',')
            .Any(part =>
            {
                var text = part.Trim();
                if (IsLoopback(text))
                    return true;
                var inline = PostgresEndpoint().Match(text);
                if (inline.Success && IsLoopback(inline.Groups["host"].Value))
                    return true;
                var url = UrlEndpoint().Match(text);
                return url.Success && IsLoopback(url.Groups["host"].Value);
            });
}
