using System.Net;
using System.Net.Sockets;

namespace Abox.UserSecrets;

public sealed partial class UserSecretsCopy
{
    private const string DatabaseHostKey = "Database_Config:Host";
    private const string DatabasePortKey = "Database_Config:Port";

    private static IReadOnlyDictionary<string, string> SelectedSplitSettings(
        UserSecretSnapshot store,
        IReadOnlyList<ProjectSettings> projects,
        HashSet<int> ports,
        IReadOnlyList<string> unresolvedProjects
    )
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (store.Values.TryGetValue(DatabasePortKey, out var port) && port is not null)
            result.Add(DatabasePortKey, port);
        if (!store.Values.TryGetValue(DatabaseHostKey, out var host) || host is null)
            return result;
        if (projects.Count == 0)
        {
            result.Add(
                DatabaseHostKey,
                RewriteSetting(host, ports, store.Id, DatabaseHostKey, store.Values)
            );
            return result;
        }
        var consumers = new List<ProjectOverrides>();
        foreach (var project in projects)
        {
            consumers.Add(
                BuildProjectOverrides(project, store, ports, [new(DatabaseHostKey, host)])
            );
        }
        // A selected host alone still derives its destination from consumer-specific port metadata.
        var adapted = MergeGeneratedOverrides(
            store.Id,
            consumers,
            store.Values.ContainsKey(DatabasePortKey) ? [] : unresolvedProjects
        );
        result.Add(
            DatabaseHostKey,
            adapted.TryGetValue(DatabaseHostKey, out var adaptedHost) ? adaptedHost : host
        );
        return result;
    }

    private static Dictionary<string, string?> EffectiveSettings(
        ProjectSettings project,
        UserSecretSnapshot store
    )
    {
        var effective = new Dictionary<string, string?>(
            project.AllSettings,
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
        if (key.Equals(DatabasePortKey, StringComparison.OrdinalIgnoreCase))
            return value;
        if (!key.Equals(DatabaseHostKey, StringComparison.OrdinalIgnoreCase))
            return RewriteEndpoint(value, ports, id, key, project);
        if (!effective.TryGetValue(DatabasePortKey, out var portText) || portText is null)
            return value;
        if (IsSplitLoopback(value) && IsMappedEndpointPort(portText, ports))
            return "host.docker.internal";
        if (
            portText.Split(',').Any(part => IsMappedEndpointPort(part.Trim(), ports))
            && HasSplitLoopback(value)
        )
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
