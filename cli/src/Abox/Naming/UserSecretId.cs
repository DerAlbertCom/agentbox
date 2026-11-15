using System.Text.RegularExpressions;

namespace Abox.Naming;

/// <summary>A portable user-secret store directory name, never a path.</summary>
internal static partial class UserSecretId
{
    internal static void Validate(string id)
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
