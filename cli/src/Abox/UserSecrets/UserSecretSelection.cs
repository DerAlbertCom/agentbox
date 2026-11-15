using System.Text.Json;
using System.Text.RegularExpressions;

namespace Abox.UserSecrets;

/// <summary>An explicitly named store. Filter syntax is rejected until supported.</summary>
public sealed partial class UserSecretSelection
{
    private UserSecretSelection(string id) => Id = id;

    public string Id { get; }

    internal static IReadOnlyList<UserSecretSelection> Parse(JsonElement entries)
    {
        if (entries.ValueKind != JsonValueKind.Array)
            throw new AboxException("abox.json: 'userSecrets' must be an array of store IDs.");
        var result = new List<UserSecretSelection>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries.EnumerateArray())
        {
            if (entry.ValueKind == JsonValueKind.Object)
                throw new AboxException(
                    "abox.json: filtered user-secret entries are not supported yet; no stores were copied. Use a plain ID only when the entire store is intended."
                );
            if (entry.ValueKind != JsonValueKind.String)
                throw new AboxException(
                    "abox.json: every user-secret entry must be a plain store ID."
                );
            var id = entry.GetString()!;
            ValidateId(id);
            if (!ids.Add(id))
                throw new AboxException(
                    "abox.json: duplicate user-secret store IDs are not allowed (case-insensitive)."
                );
            result.Add(new UserSecretSelection(id));
        }
        return result;
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
