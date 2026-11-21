using System.Text;

namespace Abox.Kits;

/// <summary>Quotes generated YAML scalars without JSON's invalid YAML surrogate escapes.</summary>
internal static class YamlScalar
{
    internal static string Quote(string value)
    {
        var quoted = new StringBuilder("\"");
        foreach (var character in value)
        {
            quoted.Append(
                character switch
                {
                    '"' => "\\\"",
                    '\\' => "\\\\",
                    <= '\u001f'
                    or >= '\u007f'
                    and <= '\u009f'
                    or '\u2028'
                    or '\u2029'
                    or '\uFEFF'
                    or '\uFFFE'
                    or '\uFFFF' => $"\\u{(int)character:X4}",
                    _ => character.ToString(),
                }
            );
        }
        return quoted.Append('"').ToString();
    }
}
