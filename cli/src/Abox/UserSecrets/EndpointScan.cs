namespace Abox.UserSecrets;

internal enum EndpointScanKind
{
    Unrecognized,
    Unchanged,
    UnsafeMappedLocal,
    ReplaceHosts,
}

// Positions always refer to the original string, never decoded or normalized text.
internal readonly record struct EndpointSourceSpan(int Start, int Length);

// Recognized-but-unchanged stops dispatch too: another grammar must not reinterpret it.
internal sealed record EndpointScan(EndpointScanKind Kind, IReadOnlyList<EndpointSourceSpan> Hosts)
{
    internal static readonly EndpointScan Unrecognized = new(EndpointScanKind.Unrecognized, []);
    internal static readonly EndpointScan Unchanged = new(EndpointScanKind.Unchanged, []);
    internal static readonly EndpointScan Unsafe = new(EndpointScanKind.UnsafeMappedLocal, []);

    internal static EndpointScan Replace(int start, int length) =>
        new(EndpointScanKind.ReplaceHosts, [new(start, length)]);
}
