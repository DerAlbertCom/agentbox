using System.Globalization;
using System.Text.Json;
using static Abox.UserSecrets.SharedStoreOverrides;

namespace Abox.UserSecrets;

/// <summary>Pure per-store construction. Payloads stay opaque; only Summary is value-free.</summary>
internal sealed class UserSecretStoreTarget
{
    private UserSecretStoreTarget(
        UserSecretSnapshot snapshot,
        IReadOnlyList<string> rewrittenKeys,
        bool safetyOverride
    )
    {
        Snapshot = snapshot;
        Summary = new UserSecretsStoreReport(snapshot.Id, snapshot.KeyCount)
        {
            RewrittenKeys = rewrittenKeys,
        };
        SafetyOverride = safetyOverride;
    }

    internal UserSecretSnapshot Snapshot { get; }
    internal UserSecretsStoreReport Summary { get; }
    internal bool SafetyOverride { get; }

    internal static UserSecretStoreTarget Construct(
        UserSecretSnapshot store,
        IReadOnlyList<ProjectSettings> projects,
        HashSet<int> ports,
        UserSecretOriginalPolicy policy
    )
    {
        var selectedTargets = SelectedSecretTargets.Build(store, projects, ports);
        var overrides = new List<ProjectOverrides>();
        var safetyOverride = false;
        foreach (var project in projects)
        {
            var candidate = BuildProjectOverrides(
                project,
                store,
                ports,
                project.SelectedSettings.Values.Where(pair => !store.Values.ContainsKey(pair.Key))
            );
            // Selected host originals were checked before runtime access. Every generated
            // appsettings original is checked, even when shared consumers agree.
            foreach (var key in candidate.GeneratedValues.Keys)
                safetyOverride |= policy.Check(
                    store.Id,
                    key,
                    project.SelectedSettings.Values[key],
                    project.Project
                );
            overrides.Add(candidate);
        }
        var generated = MergeGeneratedOverrides(store.Id, overrides);
        return Serialize(store, selectedTargets, generated, safetyOverride);
    }

    private static UserSecretStoreTarget Serialize(
        UserSecretSnapshot store,
        IReadOnlyDictionary<string, string> selectedTargets,
        IReadOnlyDictionary<string, string> generated,
        bool safetyOverride
    )
    {
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
                WriteTarget(writer, property.Value, selectedTargets, rewritten, property.Name);
            }
            foreach (var (key, target) in generated)
            {
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
        return new UserSecretStoreTarget(snapshot, rewritten.AsReadOnly(), safetyOverride);
    }

    private static void WriteTarget(
        Utf8JsonWriter writer,
        JsonElement value,
        IReadOnlyDictionary<string, string> targets,
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
                        targets,
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
                        targets,
                        rewritten,
                        path + ":" + (index++).ToString(CultureInfo.InvariantCulture)
                    );
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                var original = value.GetString()!;
                var target = targets[path!];
                if (target != original)
                    rewritten.Add(path!);
                writer.WriteStringValue(target);
                break;
            default:
                value.WriteTo(writer);
                break;
        }
    }
}
