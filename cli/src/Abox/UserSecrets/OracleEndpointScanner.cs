using System.Text.RegularExpressions;
using static Abox.UserSecrets.EndpointSyntax;

namespace Abox.UserSecrets;

// Oracle recognition retains malformed mapped candidates until outer field and
// syntax validation have both finished. Only then is it a shared dispatch outcome.
internal static partial class OracleEndpointScanner
{
    internal static EndpointScan Scan(string value, HashSet<int> ports)
    {
        var endpoint = ScanValue(value, ports);
        return endpoint.Recognized ? endpoint.ToEndpointScan() : ScanFields(value, ports);
    }

    private static OracleHostScan ScanValue(
        string value,
        HashSet<int> ports,
        bool inConnectionString = false
    ) =>
        OracleDescriptorSyntax.Recognizes(value)
            ? OracleDescriptorScanner.Scan(value, ports)
            : OracleEasyConnectScanner.Scan(value, ports, inConnectionString);

    private static EndpointScan ScanFields(string value, HashSet<int> ports)
    {
        var scan = ConnectionStringScanner.Scan(value);
        var fields = new ConnectionEndpointFields(value, scan);
        var sources = scan.Fields.Where(IsDataSource).ToArray();
        var hosts = new List<EndpointSourceSpan>();
        var recognized = false;
        var valid = true;
        foreach (var source in sources)
        {
            var endpoint = ScanSource(value, source, ports);
            recognized |= endpoint.Recognized;
            valid &= endpoint.Valid;
            hosts.AddRange(endpoint.Hosts);
        }
        if (!scan.CompleteAndUnique && IncompleteSource(value, scan) is { } incomplete)
        {
            var endpoint = ScanSource(value, incomplete, ports);
            recognized |= endpoint.Recognized;
            hosts.AddRange(endpoint.Hosts);
        }
        valid &= sources.Length == 1 && fields.SingleHost && fields.Ports.Length == 0;
        return new OracleHostScan(recognized, valid, hosts).ToEndpointScan();
    }

    private static OracleHostScan ScanSource(
        string value,
        ConnectionField field,
        HashSet<int> ports
    )
    {
        var source = OracleSourceValue.Read(value, field);
        return source.Map(ScanValue(source.Text, ports, inConnectionString: true));
    }

    private static bool IsDataSource(ConnectionField field) =>
        field.Key.Equals("Data Source", StringComparison.OrdinalIgnoreCase);

    private static ConnectionField? IncompleteSource(string value, ConnectionStringScan scan)
    {
        // PartialField is only a prefix through StoppedAt, not a complete value.
        // Retain Oracle's bounded next-field fallback over the original remainder:
        // a malformed delimiter may stop the common scanner before the local host.
        // The shared contract defines partial spans, not a recovery grammar; using
        // that prefix here would narrow existing malformed mapped-local recognition.
        // Never search inside credentials, prose, or any other completed field.
        var fields = scan.Fields;
        var position = fields.Count == 0 ? 0 : fields[^1].Start + fields[^1].Length;
        if (position < value.Length && value[position] is '\'' or '"')
            position++;
        while (
            position < value.Length
            && (char.IsWhiteSpace(value[position]) || value[position] == ';')
        )
            position++;
        var prefix = OracleSourcePrefix().Match(value[position..]);
        if (!prefix.Success)
            return null;
        position += prefix.Length;
        return new("Data Source", position, value.Length - position);
    }

    [GeneratedRegex(
        @"\AData\s+Source\s*=\s*['"" ]?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    )]
    private static partial Regex OracleSourcePrefix();
}

internal sealed record OracleHostScan(
    bool Recognized,
    bool Valid,
    IReadOnlyList<EndpointSourceSpan> Hosts
)
{
    internal EndpointScan ToEndpointScan()
    {
        if (Hosts.Count > 0 && !Valid)
            return EndpointScan.Unsafe;
        if (!Recognized)
            return EndpointScan.Unrecognized;
        return Hosts.Count == 0
            ? EndpointScan.Unchanged
            : new(EndpointScanKind.ReplaceHosts, Hosts);
    }
}
