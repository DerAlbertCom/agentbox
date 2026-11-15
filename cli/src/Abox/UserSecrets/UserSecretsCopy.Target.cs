using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Abox.UserSecrets;

public sealed partial class UserSecretsCopy
{
    // Target-specific discovery/rewriting belongs here, after original-value validation.
    // Prepare all target payloads before CopyAsync starts its first write.
    private async Task<IReadOnlyList<UserSecretSnapshot>> PrepareTargetAsync(
        PreparedUserSecrets prepared,
        string sandbox,
        CancellationToken cancellationToken
    )
    {
        var ports = (
            await sbx.GetUserSecretsHostPortsAsync(sandbox, cancellationToken)
        ).ToHashSet();
        var targets = new List<UserSecretSnapshot>();
        foreach (var store in prepared.Stores)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rewritten = new List<string>();
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
                WriteTarget(writer, store.Root, ports, rewritten);
            using var document = JsonDocument.Parse(stream.ToArray());
            targets.Add(
                rewritten.Count == 0
                    ? store
                    : new UserSecretSnapshot(store.Id, document.RootElement, store.Values)
            );
        }
        await sbx.ValidateUserSecretsDestinationAsync(
            sandbox,
            prepared.Stores.Select(store => store.Id).ToArray(),
            cancellationToken
        );
        return targets;
    }

    private static void WriteTarget(
        Utf8JsonWriter writer,
        JsonElement value,
        HashSet<int> ports,
        List<string> rewritten,
        string? path = null
    )
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject())
                {
                    writer.WritePropertyName(property.Name);
                    WriteTarget(
                        writer,
                        property.Value,
                        ports,
                        rewritten,
                        path is null ? property.Name : path + ":" + property.Name
                    );
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                var index = 0;
                foreach (var item in value.EnumerateArray())
                    WriteTarget(
                        writer,
                        item,
                        ports,
                        rewritten,
                        path + ":" + (index++).ToString(CultureInfo.InvariantCulture)
                    );
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                var original = value.GetString()!;
                var target = RewriteEndpoint(original, ports);
                if (target != original)
                    rewritten.Add(path!);
                writer.WriteStringValue(target);
                break;
            default:
                value.WriteTo(writer);
                break;
        }
    }

    private static string RewriteEndpoint(string value, HashSet<int> ports)
    {
        var match = UrlEndpoint().Match(value);
        if (match.Success)
        {
            if (
                value.Any(character =>
                    char.IsWhiteSpace(character) || char.IsControl(character) || character == '\\'
                )
                || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
                || !uri.IsWellFormedOriginalString()
                || !ports.Contains(
                    int.Parse(match.Groups["port"].Value, CultureInfo.InvariantCulture)
                )
            )
                return value;
            var host = match.Groups["host"];
            return ReplaceHost(value, host.Index, host.Length);
        }
        var fields = ConnectionFields(value);
        if (fields is null)
            return value;
        var hosts = fields.Where(field => HostFields.Contains(field.Key)).ToArray();
        if (hosts.Length != 1)
            return value;
        var endpoint = hosts[0];
        var text = value.Substring(endpoint.Start, endpoint.Length);
        var separatePort = fields.SingleOrDefault(field =>
            field.Key.Equals("Port", StringComparison.OrdinalIgnoreCase)
        );
        if (separatePort is not null)
        {
            if (
                !(
                    text.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                    || text == "127.0.0.1"
                )
                || !int.TryParse(
                    value.AsSpan(separatePort.Start, separatePort.Length),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var port
                )
                || !ports.Contains(port)
                || !(
                    endpoint.Key.Equals("Server", StringComparison.OrdinalIgnoreCase)
                    || endpoint.Key.Equals("Host", StringComparison.OrdinalIgnoreCase)
                )
            )
                return value;
            return ReplaceHost(value, endpoint.Start, endpoint.Length);
        }
        if (endpoint.Key.Equals("Host", StringComparison.OrdinalIgnoreCase))
            return value;
        var sql = SqlEndpoint().Match(text);
        if (
            !sql.Success
            || !ports.Contains(int.Parse(sql.Groups["port"].Value, CultureInfo.InvariantCulture))
        )
            return value;
        return ReplaceHost(
            value,
            endpoint.Start + sql.Groups["host"].Index,
            sql.Groups["host"].Length
        );
    }

    private static string ReplaceHost(string value, int start, int length) =>
        string.Concat(value.AsSpan(0, start), "host.docker.internal", value.AsSpan(start + length));

    private static readonly HashSet<string> HostFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "Server",
        "Data Source",
        "Address",
        "Addr",
        "Network Address",
        "Host",
    };

    private sealed record ConnectionField(string Key, int Start, int Length);

    // Keep source spans rather than rebuilding connection strings (quoting/order/credentials matter).
    private static List<ConnectionField>? ConnectionFields(string value)
    {
        var fields = new List<ConnectionField>();
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var position = 0;
        while (position < value.Length)
        {
            while (
                position < value.Length
                && (char.IsWhiteSpace(value[position]) || value[position] == ';')
            )
                position++;
            if (position == value.Length)
                break;
            var keyStart = position;
            while (position < value.Length && value[position] != '=')
            {
                var character = value[position++];
                if (!(char.IsAsciiLetterOrDigit(character) || character is ' ' or '_' or '-'))
                    return null;
            }
            if (position == value.Length)
                return null;
            var key = value[keyStart..position].Trim();
            if (key.Length == 0 || !keys.Add(key))
                return null;
            position++;
            while (position < value.Length && char.IsWhiteSpace(value[position]))
                position++;
            var start = position;
            int end;
            if (position < value.Length && value[position] is '\'' or '"')
            {
                var quote = value[position++];
                start = position;
                while (position < value.Length)
                {
                    if (value[position] != quote)
                    {
                        position++;
                        continue;
                    }
                    if (position + 1 < value.Length && value[position + 1] == quote)
                    {
                        position += 2;
                        continue;
                    }
                    break;
                }
                if (position == value.Length)
                    return null;
                end = position++;
                while (position < value.Length && char.IsWhiteSpace(value[position]))
                    position++;
                if (position < value.Length && value[position] != ';')
                    return null;
            }
            else
            {
                while (position < value.Length && value[position] != ';')
                {
                    if (value[position] is '\'' or '"' or '{' or '}')
                        return null;
                    position++;
                }
                end = position;
                while (end > start && char.IsWhiteSpace(value[end - 1]))
                    end--;
            }
            fields.Add(new ConnectionField(key, start, end - start));
        }
        return fields;
    }

    [GeneratedRegex(
        @"\A[a-z][a-z0-9+.-]*://(?:[^\s/@?#]*@)?(?<host>localhost|127\.0\.0\.1):(?<port>[0-9]{1,5})(?=[/?#]|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    )]
    private static partial Regex UrlEndpoint();

    [GeneratedRegex(
        @"\A(?:tcp:)?(?<host>localhost|127\.0\.0\.1),(?<port>[0-9]{1,5})\z",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    )]
    private static partial Regex SqlEndpoint();
}
