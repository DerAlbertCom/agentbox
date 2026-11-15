using System.Text.Json;

namespace Abox.UserSecrets;

public sealed partial class UserSecretsCopy
{
    // Format adapters resolve precedence/filtering first. EffectiveValues includes unchanged
    // consumer values and adapted targets; GeneratedValues contains only keys needing writes.
    private sealed class ProjectOverrides(
        string project,
        IReadOnlyDictionary<string, string?> effectiveValues,
        IReadOnlyDictionary<string, string> generatedValues
    )
    {
        internal string Project { get; } = project;
        internal IReadOnlyDictionary<string, string?> EffectiveValues { get; } = effectiveValues;
        internal IReadOnlyDictionary<string, string> GeneratedValues { get; } = generatedValues;
    }

    private static IReadOnlyDictionary<string, string> MergeGeneratedOverrides(
        string id,
        IReadOnlyList<ProjectOverrides> projects,
        IReadOnlyList<string> unresolvedProjects
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
            if (unresolvedProjects.Count > 0)
                throw new AboxException(
                    $"{DescribeSetting(id, key, generators[0].Project)}: cannot generate overrides because consumers could not be determined for projects with unreadable XML or unresolved UserSecretsId associations ({string.Join(", ", unresolvedProjects.Order(StringComparer.Ordinal).Select(path => JsonSerializer.Serialize(path)))}). Resolve their associations with unconditional literal IDs or fix their XML/permissions and ordinary-file status, or supply authoritative selected host-secret values for copy-only behavior; no stores were copied."
                );
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
}
