using System.Text.RegularExpressions;
using static Abox.UserSecrets.EndpointSyntax;

namespace Abox.UserSecrets;

internal static partial class SqlServerEndpointScanner
{
    internal static EndpointScan Scan(
        ConnectionEndpointFields fields,
        ConnectionField field,
        HashSet<int> ports
    )
    {
        var match = SqlEndpoint().Match(fields.Text(field));
        if (!match.Success || !IsMappedEndpointPort(match.Groups["port"].Value, ports))
            return EndpointScan.Unrecognized;
        if (
            !fields.SingleHost
            || fields.Ports.Length != 0
            || field.Key.Equals("Host", StringComparison.OrdinalIgnoreCase)
        )
            return EndpointScan.Unsafe;
        var host = match.Groups["host"];
        return EndpointScan.Replace(field.Start + host.Index, host.Length);
    }

    [GeneratedRegex(
        @"\A(?:tcp:)?(?<host>localhost|127\.0\.0\.1),(?<port>[0-9]{1,5})\z",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    )]
    private static partial Regex SqlEndpoint();
}
