using System.Text.Json;
using Abox.Naming;

namespace Abox.Configuration;

/// <summary>An exact store ID or literal prefix selection, optionally restricted to effective .NET keys.</summary>
public sealed class UserSecretSelection
{
    private readonly string[][]? patterns;

    private UserSecretSelection(string id, string[]? keys)
    {
        Id = id;
        patterns = keys?.Select(key => key.Split('*')).ToArray();
    }

    private UserSecretSelection(
        string id,
        UserSecretSelection selection,
        UserSecretSelection? previous
    )
    {
        Id = id;
        patterns =
            previous is null ? selection.patterns
            : previous.patterns is null || selection.patterns is null ? null
            : [.. previous.patterns, .. selection.patterns];
    }

    public string Id { get; }
    internal bool IsPrefixPattern => Id.EndsWith('*');
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
            ValidateIdSelection(selection.Id);
            if (!ids.Add(selection.Id))
                throw new AboxException(
                    "abox.json: duplicate user-secret store IDs are not allowed (case-insensitive)."
                );
            result.Add(selection);
        }
        return result;
    }

    private static void ValidateIdSelection(string id)
    {
        if (!id.Contains('*'))
        {
            UserSecretId.Validate(id);
            return;
        }
        // A prefix can end in a dot (Cape.*); actual expanded IDs still cannot.
        if (id.EndsWith('*') && id.Length <= 129 && UserSecretId.IsValid(id[..^1].TrimEnd('.')))
            return;
        throw new AboxException(
            "Unsafe user-secret ID pattern. Use a safe literal prefix followed by one terminal '*'; no paths or other wildcards."
        );
    }

    internal static IReadOnlyList<UserSecretSelection> Expand(
        IReadOnlyList<UserSecretSelection> selections,
        IReadOnlyList<string> hostIds
    )
    {
        var result = new List<UserSecretSelection>();
        var indices = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var selection in selections)
        {
            var matches = hostIds
                .Where(id =>
                    selection.IsPrefixPattern
                        ? id.StartsWith(selection.Id[..^1], StringComparison.OrdinalIgnoreCase)
                        : id.Equals(selection.Id, StringComparison.OrdinalIgnoreCase)
                )
                .ToArray();
            if (!selection.IsPrefixPattern && matches.Length == 0)
                matches = [selection.Id];
            foreach (var id in matches)
            {
                UserSecretId.Validate(id);
                if (indices.TryGetValue(id, out var index))
                {
                    var previous = result[index];
                    if (!previous.Id.Equals(id, StringComparison.Ordinal))
                        throw new AboxException(
                            "Host user-secret store IDs collide case-insensitively. Rename the conflicting host directories; no stores were copied."
                        );
                    result[index] = new UserSecretSelection(id, selection, previous);
                }
                else
                {
                    indices.Add(id, result.Count);
                    result.Add(new UserSecretSelection(id, selection, null));
                }
            }
        }
        return result.ToArray();
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
}
