using System.Text;

namespace Abox.UserSecrets;

// Outer connection-string quoting is unrelated to Oracle leaf quoting. Decode
// only for interpretation; each boundary maps back to the untouched original.
internal sealed class OracleSourceValue(string text, int offset, IReadOnlyList<int>? positions)
{
    internal string Text { get; } = text;

    internal static OracleSourceValue Read(string value, ConnectionField source)
    {
        var quote = source.Start > 0 ? value[source.Start - 1] : '\0';
        if (quote is not ('\'' or '"'))
            return new(value.Substring(source.Start, source.Length), source.Start, null);
        var decoded = new StringBuilder(source.Length);
        var boundaries = new List<int>();
        for (var index = source.Start; index < source.Start + source.Length; index++)
        {
            boundaries.Add(index);
            decoded.Append(value[index]);
            if (
                value[index] == quote
                && index + 1 < source.Start + source.Length
                && value[index + 1] == quote
            )
                index++;
        }
        boundaries.Add(source.Start + source.Length);
        return new(decoded.ToString(), source.Start, boundaries);
    }

    internal OracleHostScan Map(OracleHostScan endpoint) =>
        endpoint with
        {
            Hosts = endpoint
                .Hosts.Select(host => new EndpointSourceSpan(
                    OriginalPosition(host.Start),
                    OriginalPosition(host.Start + host.Length) - OriginalPosition(host.Start)
                ))
                .ToArray(),
        };

    private int OriginalPosition(int position) =>
        positions is null ? offset + position : positions[position];
}
