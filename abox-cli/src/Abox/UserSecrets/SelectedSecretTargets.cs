using static Abox.UserSecrets.EndpointAdaptation;
using static Abox.UserSecrets.SharedStoreOverrides;
using static Abox.UserSecrets.UserSecretOriginalPolicy;

namespace Abox.UserSecrets;

internal static class SelectedSecretTargets
{
    internal static IReadOnlyDictionary<string, string> Build(
        UserSecretSnapshot store,
        IReadOnlyList<ProjectSettings> projects,
        HashSet<int> ports
    )
    {
        var targets = new Dictionary<string, string>(
            SelectedSplitSettings(store, projects, ports),
            StringComparer.OrdinalIgnoreCase
        );
        foreach (var (key, value) in store.Values)
            if (value is not null && !targets.ContainsKey(key))
                targets.Add(
                    key,
                    EndpointAdaptation.Rewrite(
                        value,
                        ports,
                        key,
                        store.Values,
                        DescribeSetting(store.Id, key, null)
                    )
                );
        return targets;
    }

    private static IReadOnlyDictionary<string, string> SelectedSplitSettings(
        UserSecretSnapshot store,
        IReadOnlyList<ProjectSettings> projects,
        HashSet<int> ports
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
                EndpointAdaptation.Rewrite(
                    host,
                    ports,
                    DatabaseHostKey,
                    store.Values,
                    DescribeSetting(store.Id, DatabaseHostKey, null)
                )
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
        var adapted = MergeGeneratedOverrides(store.Id, consumers);
        result.Add(
            DatabaseHostKey,
            adapted.TryGetValue(DatabaseHostKey, out var adaptedHost) ? adaptedHost : host
        );
        return result;
    }
}
