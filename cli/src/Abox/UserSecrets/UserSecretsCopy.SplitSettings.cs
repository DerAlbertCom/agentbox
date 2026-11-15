using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace Abox.UserSecrets;

public sealed partial class UserSecretsCopy
{
    private const string DatabaseHostKey = "Database_Config:Host";
    private const string DatabasePortKey = "Database_Config:Port";

    private static IReadOnlyDictionary<string, string> SelectedSplitHosts(
        UserSecretSnapshot store,
        IReadOnlyList<ProjectSettings> projects,
        HashSet<int> ports
    )
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!store.Values.TryGetValue(DatabaseHostKey, out var host) || host is null)
            return result;
        var targets =
            projects.Count == 0
                ? new[] { RewriteSetting(host, ports, store.Id, DatabaseHostKey, store.Values) }
                : projects
                    .Select(project =>
                        RewriteSetting(
                            host,
                            ports,
                            store.Id,
                            DatabaseHostKey,
                            EffectiveSettings(project, store),
                            project.Project
                        )
                    )
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
        if (targets.Length != 1)
            throw new AboxException(
                $"{DescribeSetting(store.Id, DatabaseHostKey, null)}: split host adaptation conflicts between discovered consumers ({string.Join(", ", projects.Select(project => JsonSerializer.Serialize(project.Project)))}). Select an authoritative host and port compatible with every consumer, or use separate stores; no stores were copied."
            );
        result.Add(DatabaseHostKey, targets[0]);
        return result;
    }

    private static IReadOnlyDictionary<string, string?> EffectiveSettings(
        ProjectSettings project,
        UserSecretSnapshot store
    )
    {
        var effective = new Dictionary<string, string?>(
            project.EffectiveSettings,
            StringComparer.OrdinalIgnoreCase
        );
        foreach (var (key, value) in store.Values)
            effective[key] = value;
        return effective;
    }

    private static string RewriteSetting(
        string value,
        HashSet<int> ports,
        string id,
        string key,
        IReadOnlyDictionary<string, string?> effective,
        string? project = null
    )
    {
        if (!key.Equals(DatabaseHostKey, StringComparison.OrdinalIgnoreCase))
            return RewriteEndpoint(value, ports, id, key, project);
        if (!effective.TryGetValue(DatabasePortKey, out var portText) || portText is null)
            return value;
        bool IsMappedPort(string text) =>
            int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var port)
            && port is >= 1 and <= 65535
            && ports.Contains(port);
        if (IsSplitLoopback(value) && IsMappedPort(portText))
            return "host.docker.internal";
        if (portText.Split(',').Any(part => IsMappedPort(part.Trim())) && HasSplitLoopback(value))
            throw new AboxException(
                $"{DescribeSetting(id, key, project)}: recognized mapped-local split setting has an unsupported or ambiguous format. Use one literal loopback Host and one decimal Port in Database_Config; no stores were copied."
            );
        return value;
    }

    private static bool IsSplitLoopback(string host)
    {
        if (host.Contains('[') || host.Contains(']'))
            return host.StartsWith('[')
                && host.EndsWith(']')
                && IPAddress.TryParse(host[1..^1], out var address)
                && address.AddressFamily == AddressFamily.InterNetworkV6
                && IPAddress.IsLoopback(address);
        return IsLoopback(host);
    }

    private static bool HasSplitLoopback(string host) =>
        host.Split(',')
            .Any(part =>
            {
                var text = part.Trim();
                if (IsLoopback(text))
                    return true;
                var inline = PostgresEndpoint().Match(text);
                if (inline.Success && IsLoopback(inline.Groups["host"].Value))
                    return true;
                var url = UrlEndpoint().Match(text);
                return url.Success && IsLoopback(url.Groups["host"].Value);
            });
}
