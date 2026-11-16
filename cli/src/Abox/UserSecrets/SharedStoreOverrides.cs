using System.Text.Json;
using static Abox.UserSecrets.UserSecretOriginalPolicy;

namespace Abox.UserSecrets;

internal static class SharedStoreOverrides
{
    // Format adapters resolve precedence/filtering first. EffectiveValues includes unchanged
    // consumer values and adapted targets; GeneratedValues contains only keys needing writes.
    internal sealed class ProjectOverrides(
        string project,
        IReadOnlyDictionary<string, string?> effectiveValues,
        IReadOnlyDictionary<string, string> generatedValues
    )
    {
        internal string Project { get; } = project;
        internal IReadOnlyDictionary<string, string?> EffectiveValues { get; } = effectiveValues;
        internal IReadOnlyDictionary<string, string> GeneratedValues { get; } = generatedValues;
    }

    internal static IReadOnlyDictionary<string, string> MergeGeneratedOverrides(
        string id,
        IReadOnlyList<ProjectOverrides> projects
    )
    {
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // Canonical key spelling and output order do not depend on consumer discovery order.
        var keys = projects
            .SelectMany(project => project.GeneratedValues.Keys)
            .Order(StringComparer.Ordinal)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var key in keys)
        {
            var generators = projects
                .Where(project => project.GeneratedValues.ContainsKey(key))
                .ToArray();
            var target = generators[0].GeneratedValues[key];
            var incompatible = projects
                .Where(project =>
                    (
                        project.GeneratedValues.TryGetValue(key, out var generated)
                        && generated != target
                    )
                    || (
                        project.EffectiveValues.TryGetValue(key, out var effective)
                        && effective != target
                    )
                )
                .ToArray();
            if (incompatible.Length > 0)
                throw new AboxException(
                    $"User-secret store '{id}', key {JsonSerializer.Serialize(key)}: conflicting shared-store override for projects ({string.Join(", ", generators.Concat(incompatible).Select(project => project.Project).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Select(path => JsonSerializer.Serialize(path)))}). The generated value would replace another consumer's required effective configuration. Supply an authoritative selected host-secret value, exclude this key, or use separate stores; no stores were copied."
                );
            merged.Add(key, target);
        }
        return merged;
    }

    internal static ProjectOverrides BuildProjectOverrides(
        ProjectSettings project,
        UserSecretSnapshot store,
        HashSet<int> ports,
        IEnumerable<KeyValuePair<string, string?>> candidates
    )
    {
        // Resolve split ports before deriving desired hosts for shared-store checks.
        var effective = new Dictionary<string, string?>(
            project.AllSettings,
            StringComparer.OrdinalIgnoreCase
        );
        foreach (var (key, value) in store.Values)
            effective[key] = value;
        var generated = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, original) in candidates)
        {
            if (original is null)
                continue;
            var target = EndpointAdaptation.Rewrite(
                original,
                ports,
                key,
                effective,
                DescribeSetting(store.Id, key, project.Project)
            );
            if (target == original)
                continue;
            effective[key] = target;
            generated.Add(key, target);
        }
        return new ProjectOverrides(project.Project, effective, generated);
    }
}
