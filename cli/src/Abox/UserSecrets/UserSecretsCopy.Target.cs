using System.Globalization;
using System.Text.Json;

namespace Abox.UserSecrets;

public sealed partial class UserSecretsCopy
{
    // Discover authorization and prepare every target before CopyAsync starts its first write.
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
            var projects = prepared
                .Projects.Where(project =>
                    project.SelectedSettings.Id.Equals(store.Id, StringComparison.OrdinalIgnoreCase)
                )
                .ToArray();
            var rewritten = new List<string>();
            var selectedTargets = SelectedSettingTargets(store, projects, ports);
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
                var projectOverrides = new List<ProjectOverrides>();
                foreach (var project in projects)
                {
                    var overrides = BuildProjectOverrides(
                        project,
                        store,
                        ports,
                        project.SelectedSettings.Values.Where(pair =>
                            !store.Values.ContainsKey(pair.Key)
                        )
                    );
                    // Selected host originals were checked during PrepareAsync. Check every
                    // generated appsettings original, even when shared consumers agree.
                    foreach (var key in overrides.GeneratedValues.Keys)
                    {
                        safetyOverride |= CheckOriginal(
                            store.Id,
                            key,
                            project.SelectedSettings.Values[key],
                            prepared.Force,
                            project.Project
                        );
                    }
                    projectOverrides.Add(overrides);
                }
                foreach (var (key, target) in MergeGeneratedOverrides(store.Id, projectOverrides))
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
            "Cannot prepare user-secret destinations or rewrite policy. No stores were copied. Check 'sbx policy ls <sandbox> --type network --json' and 'sbx policy check network --sandbox <sandbox> --protocol tcp --json localhost:<port>' for complete, valid metadata, and the application user's absolute HOME/APPDATA, ownership, ordinary paths and shell/coreutils; fix the runtime and retry 'abox user-secrets'. No policy was changed; child diagnostics were suppressed."
        );

    private static IReadOnlyDictionary<string, string> SelectedSettingTargets(
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

    private static ProjectOverrides BuildProjectOverrides(
        ProjectSettings project,
        UserSecretSnapshot store,
        HashSet<int> ports,
        IEnumerable<KeyValuePair<string, string?>> candidates
    )
    {
        // Resolve split ports before deriving desired hosts for shared-store checks.
        var effective = EffectiveSettings(project, store);
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
