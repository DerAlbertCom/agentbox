using System.Text.RegularExpressions;

namespace Abox.Kits;

/// <summary>
/// The <c>${{ parameters.NAME }}</c> references a kit or an MCP declaration may use.
/// </summary>
public static partial class ParameterReference
{
    /// <summary>
    /// Replaces every parameter reference with its value. A reference to a parameter no
    /// configuration declares is an error.
    /// </summary>
    public static string Expand(string value, IReadOnlyDictionary<string, string> parameters, string source)
    {
        return ReferencePattern().Replace(value, match =>
        {
            var name = match.Groups[1].Value;
            if (!parameters.TryGetValue(name, out var parameter))
            {
                throw new AboxException($"'{source}' uses the parameter '{name}', which no configuration declares.");
            }

            return parameter;
        });
    }

    /// <summary>Whether the value uses any parameter.</summary>
    public static bool IsUsed(string value) => ReferencePattern().IsMatch(value);

    [GeneratedRegex(@"\$\{\{\s*parameters\.([A-Za-z_][A-Za-z0-9_-]*)\s*\}\}")]
    private static partial Regex ReferencePattern();
}
