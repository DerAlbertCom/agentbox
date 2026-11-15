using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Abox.UserSecrets;

public sealed partial class UserSecretsCopy
{
    // Target-specific discovery/rewriting belongs here, after original-value validation.
    // Prepare all target payloads before CopyAsync starts its first write.
    private sealed record UserSecretsTargetStore(
        UserSecretSnapshot Snapshot,
        IReadOnlyList<string> RewrittenKeys
    );

    private async Task<(
        IReadOnlyList<UserSecretsTargetStore> Stores,
        bool SafetyOverride
    )> PrepareTargetAsync(
        PreparedUserSecrets prepared,
        string sandbox,
        CancellationToken cancellationToken
    )
    {
        HashSet<int> ports;
        try
        {
            ports = (
                await sbx.GetUserSecretsHostPortsAsync(sandbox, cancellationToken)
            ).ToHashSet();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            throw TargetPreparationFailure();
        }
        var safetyOverride = prepared.SafetyOverride;
        var targets = new List<UserSecretsTargetStore>();
        foreach (var store in prepared.Stores)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rewritten = new List<string>();
            var values = new Dictionary<string, string?>(
                store.Values,
                StringComparer.OrdinalIgnoreCase
            );
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                foreach (var property in store.Root.EnumerateObject())
                {
                    writer.WritePropertyName(property.Name);
                    WriteTarget(writer, property.Value, ports, rewritten, store.Id, property.Name);
                }
                foreach (var project in store.Projects)
                foreach (var (key, original) in project.Settings.Values)
                {
                    if (store.Values.ContainsKey(key) || original is null)
                        continue;
                    var target = RewriteEndpoint(original, ports, store.Id, key, project.Project);
                    if (target == original)
                        continue;
                    if (store.Projects.Count > 1)
                        throw new AboxException(
                            $"User-secret store '{store.Id}', key {JsonSerializer.Serialize(key)}, has multiple discovered consumers ({string.Join(", ", store.Projects.Select(consumer => JsonSerializer.Serialize(consumer.Project)))}). Generating overrides for a shared store is a temporary unsupported case. Supply an authoritative selected host-secret value or use separate stores; no stores were copied."
                        );
                    safetyOverride |= CheckOriginal(
                        store.Id,
                        key,
                        original,
                        prepared.Force,
                        project.Project
                    );
                    writer.WriteString(key, target);
                    values.Add(key, target);
                    rewritten.Add(key);
                }
                writer.WriteEndObject();
            }
            using var document = JsonDocument.Parse(stream.ToArray());
            var snapshot =
                rewritten.Count == 0
                    ? store
                    : new UserSecretSnapshot(store.Id, document.RootElement, values);
            targets.Add(new UserSecretsTargetStore(snapshot, rewritten.AsReadOnly()));
        }
        try
        {
            await sbx.ValidateUserSecretsDestinationAsync(
                sandbox,
                prepared.Stores.Select(store => store.Id).ToArray(),
                cancellationToken
            );
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            throw TargetPreparationFailure();
        }
        return (targets, safetyOverride);
    }

    private static AboxException TargetPreparationFailure() =>
        new(
            "Cannot prepare user-secret destinations or rewrite policy. No stores were copied. Check 'sbx policy ls <sandbox> --source kit --type network --json' and 'sbx policy check network --sandbox <sandbox> --protocol tcp --json localhost:<port>' for complete, valid metadata, and the application user's absolute HOME/APPDATA, ownership, ordinary paths and shell/coreutils; fix the runtime and retry 'abox user-secrets'. No policy was changed; child diagnostics were suppressed."
        );

    private static void WriteTarget(
        Utf8JsonWriter writer,
        JsonElement value,
        HashSet<int> ports,
        List<string> rewritten,
        string id,
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
                        id,
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
                        id,
                        path + ":" + (index++).ToString(CultureInfo.InvariantCulture)
                    );
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                var original = value.GetString()!;
                var target = RewriteEndpoint(original, ports, id, path!);
                if (target != original)
                    rewritten.Add(path!);
                writer.WriteStringValue(target);
                break;
            default:
                value.WriteTo(writer);
                break;
        }
    }

    private static string RewriteEndpoint(
        string value,
        HashSet<int> ports,
        string id,
        string key,
        string? project = null
    )
    {
        AboxException Unsupported() =>
            new(
                $"User-secret store '{id}', key {JsonSerializer.Serialize(key)}{(project is null ? "" : $", project {JsonSerializer.Serialize(project)}")}: recognized mapped-local endpoint has an unsupported or ambiguous format. Use a well-formed absolute URL or single-host PostgreSQL connection string with one port; no stores were copied."
            );
        var match = UrlEndpoint().Match(value);
        if (match.Success)
        {
            var host = match.Groups["host"];
            if (!IsLoopback(host.Value))
                return value;
            var parsed = Uri.TryCreate(value, UriKind.Absolute, out var uri);
            int port;
            if (match.Groups["port"].Success)
            {
                if (
                    !int.TryParse(
                        match.Groups["port"].Value,
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out port
                    )
                )
                    return value;
            }
            else if (Uri.TryCreate(value[..match.Length], UriKind.Absolute, out var authority))
                port = authority.Port;
            else
                return value;
            if (!ports.Contains(port))
                return value;
            if (
                value.Any(character =>
                    char.IsWhiteSpace(character) || char.IsControl(character) || character == '\\'
                )
                || !parsed
                || !uri!.IsWellFormedOriginalString()
            )
                throw Unsupported();
            return ReplaceHost(value, host.Index, host.Length);
        }
        var fields = ConnectionFields(value, out var complete);
        var hosts = fields.Where(field => HostFields.Contains(field.Key)).ToArray();
        var separatePorts = fields
            .Where(field => field.Key.Equals("Port", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        string Text(ConnectionField field) => value.Substring(field.Start, field.Length);
        foreach (var endpoint in hosts)
        {
            var text = Text(endpoint);
            var sql = SqlEndpoint().Match(text);
            if (
                sql.Success
                && ports.Contains(int.Parse(sql.Groups["port"].Value, CultureInfo.InvariantCulture))
            )
            {
                if (
                    !complete
                    || hosts.Length != 1
                    || separatePorts.Length != 0
                    || endpoint.Key.Equals("Host", StringComparison.OrdinalIgnoreCase)
                )
                    throw Unsupported();
                return ReplaceHost(
                    value,
                    endpoint.Start + sql.Groups["host"].Index,
                    sql.Groups["host"].Length
                );
            }
            var hostParts = text.Split(',');
            var portParts = separatePorts.SelectMany(field => Text(field).Split(',')).ToArray();
            var postgres = endpoint.Key.Equals("Host", StringComparison.OrdinalIgnoreCase);
            var server = endpoint.Key.Equals("Server", StringComparison.OrdinalIgnoreCase);
            if (!postgres && !server)
                continue;
            if (postgres)
            {
                foreach (var part in hostParts)
                {
                    var inline = PostgresEndpoint().Match(part);
                    if (
                        !inline.Success
                        || !IsLoopback(inline.Groups["host"].Value)
                        || !int.TryParse(
                            inline.Groups["port"].Value,
                            NumberStyles.None,
                            CultureInfo.InvariantCulture,
                            out var inlinePort
                        )
                        || !ports.Contains(inlinePort)
                    )
                        continue;
                    if (
                        !complete
                        || hosts.Length != 1
                        || hostParts.Length != 1
                        || separatePorts.Length != 0
                    )
                        throw Unsupported();
                    return ReplaceHost(
                        value,
                        endpoint.Start + inline.Groups["host"].Index,
                        inline.Groups["host"].Length
                    );
                }
            }
            var mappedPort =
                separatePorts.Length == 0
                    ? postgres && ports.Contains(5432)
                    : portParts.Any(part =>
                        int.TryParse(
                            part,
                            NumberStyles.None,
                            CultureInfo.InvariantCulture,
                            out var port
                        ) && ports.Contains(port)
                    );
            if (!hostParts.Any(part => IsLoopback(part.Trim())) || !mappedPort)
                continue;
            if (
                !complete
                || hosts.Length != 1
                || hostParts.Length != 1
                || separatePorts.Length > 1
                || portParts.Length > 1
            )
                throw Unsupported();
            var hostStart = text.Length - text.TrimStart().Length;
            return ReplaceHost(value, endpoint.Start + hostStart, text.Trim().Length);
        }
        return value;
    }

    private static string ReplaceHost(string value, int start, int length) =>
        string.Concat(value.AsSpan(0, start), "host.docker.internal", value.AsSpan(start + length));

    private static bool IsLoopback(string host)
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
    private static List<ConnectionField> ConnectionFields(string value, out bool complete)
    {
        complete = false;
        var fields = new List<ConnectionField>();
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var duplicate = false;
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
                    return fields;
            }
            if (position == value.Length)
                return fields;
            var key = value[keyStart..position].Trim();
            if (key.Length == 0)
                return fields;
            duplicate |= !keys.Add(key);
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
                    return fields;
                end = position++;
                while (position < value.Length && char.IsWhiteSpace(value[position]))
                    position++;
                if (position < value.Length && value[position] != ';')
                    return fields;
            }
            else
            {
                while (position < value.Length && value[position] != ';')
                {
                    if (value[position] is '\'' or '"' or '{' or '}')
                        return fields;
                    position++;
                }
                end = position;
                while (end > start && char.IsWhiteSpace(value[end - 1]))
                    end--;
            }
            fields.Add(new ConnectionField(key, start, end - start));
        }
        complete = !duplicate;
        return fields;
    }

    [GeneratedRegex(
        @"\A[a-z][a-z0-9+.-]*://(?:[^\s/@?#]*@)?(?<host>\[[^\]]+\]|[^\s:/?#\\]+)(?::(?<port>[0-9]+))?(?=[/?#\\\s]|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    )]
    private static partial Regex UrlEndpoint();

    [GeneratedRegex(
        @"\A(?<host>\[[^\]]+\]|[^:,]+):(?<port>[0-9]+)\z",
        RegexOptions.CultureInvariant
    )]
    private static partial Regex PostgresEndpoint();

    [GeneratedRegex(
        @"\A(?:tcp:)?(?<host>localhost|127\.0\.0\.1),(?<port>[0-9]{1,5})\z",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    )]
    private static partial Regex SqlEndpoint();
}
