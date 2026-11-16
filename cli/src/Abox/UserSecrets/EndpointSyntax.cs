using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace Abox.UserSecrets;

internal static partial class EndpointSyntax
{
    internal static bool TryParseEndpointPort(ReadOnlySpan<char> text, out int port) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out port)
        && port is >= 1 and <= 65535;

    internal static bool IsMappedEndpointPort(ReadOnlySpan<char> text, HashSet<int> ports) =>
        TryParseEndpointPort(text, out var port) && ports.Contains(port);

    internal static string ReplaceHost(string value, int start, int length) =>
        string.Concat(value.AsSpan(0, start), "host.docker.internal", value.AsSpan(start + length));

    internal static bool IsLoopback(string host)
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

    internal static readonly HashSet<string> HostFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "Server",
        "Data Source",
        "Address",
        "Addr",
        "Network Address",
        "Host",
    };

    [GeneratedRegex(
        @"\A[a-z][a-z0-9+.-]*://(?:[^\s/@?#]*@)?(?<host>\[[^\]]+\]|[^\s:/?#\\]+)(?::(?<port>[0-9]+))?(?=[/?#\\\s]|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    )]
    internal static partial Regex UrlEndpoint();

    [GeneratedRegex(
        @"\A(?<host>\[[^\]]+\]|[^:,]+):(?<port>[0-9]+)\z",
        RegexOptions.CultureInvariant
    )]
    internal static partial Regex PostgresEndpoint();
}
