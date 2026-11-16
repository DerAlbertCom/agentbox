using static Abox.UserSecrets.EndpointSyntax;

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
        // Preserve dispatch priority. In particular, recognized unchanged PostgreSQL
        // query/compound URLs must not fall through to generic authority rewriting.
        var scan = OracleEndpointScanner.Scan(value, ports);
        if (scan.Kind == EndpointScanKind.Unrecognized)
            scan = PostgresEndpointScanner.ScanUrl(value, ports);
        if (scan.Kind == EndpointScanKind.Unrecognized)
            scan = UriEndpointScanner.Scan(value, ports);
        if (scan.Kind == EndpointScanKind.Unrecognized)
            scan = ConnectionEndpointScanner.Scan(value, ports);
        if (scan.Kind == EndpointScanKind.UnsafeMappedLocal)
            throw Unsupported();
        foreach (var host in scan.Hosts.OrderByDescending(span => span.Start))
            value = ReplaceHost(value, host.Start, host.Length);
        return value;
    }
}
