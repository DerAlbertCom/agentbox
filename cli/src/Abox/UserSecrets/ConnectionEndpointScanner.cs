using static Abox.UserSecrets.EndpointSyntax;

namespace Abox.UserSecrets;

internal sealed class ConnectionEndpointFields
{
    internal ConnectionEndpointFields(string value)
    {
        Source = value;
        var scan = ConnectionStringScanner.Scan(value);
        Hosts = scan.Fields.Where(field => HostFields.Contains(field.Key)).ToArray();
        Ports = scan
            .Fields.Where(field => field.Key.Equals("Port", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        SingleHost = scan.CompleteAndUnique && Hosts.Length == 1;
    }

    internal string Source { get; }
    internal ConnectionField[] Hosts { get; }
    internal ConnectionField[] Ports { get; }
    internal bool SingleHost { get; }

    internal string Text(ConnectionField field) => Source.Substring(field.Start, field.Length);
}

internal static class ConnectionEndpointScanner
{
    internal static EndpointScan Scan(string value, HashSet<int> ports)
    {
        var fields = new ConnectionEndpointFields(value);
        foreach (var host in fields.Hosts)
        {
            var sql = SqlServerEndpointScanner.Scan(fields, host, ports);
            if (sql.Kind != EndpointScanKind.Unrecognized)
                return sql;
            var postgres = PostgresEndpointScanner.ScanField(fields, host, ports);
            if (postgres.Kind != EndpointScanKind.Unrecognized)
                return postgres;
        }
        return EndpointScan.Unchanged;
    }
}
