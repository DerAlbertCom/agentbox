using System.Text.Json;
using System.Text.RegularExpressions;

namespace Abox.UserSecrets;

/// <summary>An explicitly named store, optionally restricted to effective .NET keys.</summary>
public sealed partial class UserSecretSelection
{
    private readonly string[][]? patterns;

    private UserSecretSelection(string id, string[]? keys)
    {
        Id = id;
        patterns = keys?.Select(key => key.Split('*')).ToArray();
    }

    public string Id { get; }
    internal bool IsFiltered => patterns is not null;

    internal bool Includes(string key) =>
        patterns is null || patterns.Any(parts => Matches(key, parts));

    private static bool Matches(string key, string[] parts)
    {
        if (parts.Length == 1)
            return key.Equals(parts[0], StringComparison.OrdinalIgnoreCase);
        if (!key.StartsWith(parts[0], StringComparison.OrdinalIgnoreCase))
            return false;
        var remaining = key.AsSpan(parts[0].Length);
        for (var index = 1; index < parts.Length - 1; index++)
        {
            var match = remaining.IndexOf(parts[index], StringComparison.OrdinalIgnoreCase);
            if (match < 0)
                return false;
            remaining = remaining[(match + parts[index].Length)..];
        }
        return remaining.EndsWith(parts[^1], StringComparison.OrdinalIgnoreCase);
    }

    internal static IReadOnlyList<UserSecretSelection> Parse(JsonElement entries)
    {
        if (entries.ValueKind != JsonValueKind.Array)
            throw new AboxException(
                "abox.json: 'userSecrets' must be an array of store IDs or selection objects."
            );
        var result = new List<UserSecretSelection>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries.EnumerateArray())
        {
            var selection = entry.ValueKind switch
            {
                JsonValueKind.String => new UserSecretSelection(entry.GetString()!, null),
                JsonValueKind.Object => ParseObject(entry),
                _ => throw new AboxException(
                    "abox.json: every user-secret entry must be a store ID or an object with 'id' and optional 'keys'."
                ),
            };
            ValidateId(selection.Id);
            if (!ids.Add(selection.Id))
                throw new AboxException(
                    "abox.json: duplicate user-secret store IDs are not allowed (case-insensitive)."
                );
            result.Add(selection);
        }
        return result;
    }

    private static UserSecretSelection ParseObject(JsonElement entry)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in entry.EnumerateObject())
            if (!seen.Add(property.Name) || property.Name is not ("id" or "keys"))
                throw new AboxException(
                    "abox.json: user-secret objects allow only unique 'id' and 'keys' properties."
                );
        if (!entry.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String)
            throw new AboxException("abox.json: a user-secret object requires a string 'id'.");
        string[]? keys = null;
        if (entry.TryGetProperty("keys", out var patterns))
        {
            if (patterns.ValueKind != JsonValueKind.Array)
                throw new AboxException(
                    "abox.json: user-secret 'keys' must be an array of patterns."
                );
            keys = patterns
                .EnumerateArray()
                .Select(pattern =>
                    pattern.ValueKind == JsonValueKind.String
                        ? pattern.GetString()!
                        : throw new AboxException(
                            "abox.json: every user-secret key pattern must be a string."
                        )
                )
                .ToArray();
            if (keys.Any(key => string.IsNullOrWhiteSpace(key) || key.Any(char.IsControl)))
                throw new AboxException(
                    "abox.json: user-secret key patterns must be nonempty and contain no control characters. Use an empty 'keys' array to select no keys."
                );
        }
        return new UserSecretSelection(id.GetString()!, keys);
    }

    internal static void ValidateId(string id)
    {
        if (!SafeId().IsMatch(id) || id.EndsWith('.') || IsDeviceName(id))
            throw new AboxException(
                "Unsafe user-secret ID. Use 1–128 ASCII letters, digits, dots, underscores or hyphens, beginning with a letter or digit; no paths or reserved device names."
            );
    }

    private static bool IsDeviceName(string id)
    {
        var name = id.Split('.')[0].ToUpperInvariant();
        return name is "CON" or "PRN" or "AUX" or "NUL"
            || (
                name.Length == 4
                && (name.StartsWith("COM") || name.StartsWith("LPT"))
                && name[3] is >= '0' and <= '9'
            );
    }

    [GeneratedRegex(@"\A[A-Za-z0-9][A-Za-z0-9_.-]{0,127}\z", RegexOptions.CultureInvariant)]
    private static partial Regex SafeId();
}
