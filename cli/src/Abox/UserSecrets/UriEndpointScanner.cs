using static Abox.UserSecrets.EndpointSyntax;

namespace Abox.UserSecrets;

internal static class UriEndpointScanner
{
    internal static EndpointScan Scan(string value, HashSet<int> ports)
    {
        var match = UrlEndpoint().Match(value);
        if (!match.Success)
            return EndpointScan.Unrecognized;
        var host = match.Groups["host"];
        if (!IsLoopback(host.Value))
            return EndpointScan.Unchanged;
        var parsed = Uri.TryCreate(value, UriKind.Absolute, out var uri);
        int port;
        if (match.Groups["port"].Success)
        {
            if (!TryParseEndpointPort(match.Groups["port"].Value, out port))
                return EndpointScan.Unchanged;
        }
        else if (Uri.TryCreate(value[..match.Length], UriKind.Absolute, out var authority))
            port = authority.Port;
        else
            return EndpointScan.Unchanged;
        if (!ports.Contains(port))
            return EndpointScan.Unchanged;
        if (
            value.Any(character =>
                char.IsWhiteSpace(character) || char.IsControl(character) || character == '\\'
            )
            || !parsed
            || !uri!.IsWellFormedOriginalString()
        )
            return EndpointScan.Unsafe;
        return EndpointScan.Replace(host.Index, host.Length);
    }
}
