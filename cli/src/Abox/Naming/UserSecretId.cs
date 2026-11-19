using System.Text.RegularExpressions;

namespace Abox.Naming;

/// <summary>A portable user-secret store directory name, never a path.</summary>
internal static partial class UserSecretId
{
    internal static void Validate(string id)
    {
        if (!IsValid(id))
            throw new AboxException(
                "Unsafe user-secret ID. Use 1–128 ASCII letters, digits, dots, underscores or hyphens, beginning with a letter or digit; no paths or reserved device names."
            );
    }

    internal static bool IsValid(string id) =>
        SafeId().IsMatch(id) && !id.EndsWith('.') && !WindowsDeviceName.IsReserved(id);

    [GeneratedRegex(@"\A[A-Za-z0-9][A-Za-z0-9_.-]{0,127}\z", RegexOptions.CultureInvariant)]
    private static partial Regex SafeId();
}
