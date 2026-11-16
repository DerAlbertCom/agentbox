using System.Text.Json;

namespace Abox.UserSecrets;

/// <summary>Checks originals, never adapted values; contains no secret payloads.</summary>
internal sealed class UserSecretOriginalPolicy(IReadOnlyList<string> denyList, bool force)
{
    // Throws for denied originals without force; returns true only for an override actually used.
    internal bool EnsureAllowedAndReportForceOverrideUsed(
        string id,
        string key,
        string? value,
        string? project = null
    )
    {
        if (
            value is null
            || !denyList.Any(denied => value.Contains(denied, StringComparison.OrdinalIgnoreCase))
        )
            return false;
        if (!force)
            throw new AboxException(
                $"{DescribeSetting(id, key, project)}, is blocked by the global deny list. No stores were copied. Use --force only if this is intentional."
            );
        return true;
    }

    internal static string DescribeSetting(string id, string key, string? project) =>
        $"User-secret store '{id}', key {JsonSerializer.Serialize(key)}{(project is null ? "" : $", project {JsonSerializer.Serialize(project)}")}";
}
