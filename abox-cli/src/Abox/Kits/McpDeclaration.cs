using System.Text.RegularExpressions;

namespace Abox.Kits;

/// <summary>
/// An MCP server a squad uses.
/// </summary>
/// <param name="Name">The server name, as sbx knows it.</param>
/// <param name="Url">The endpoint of a remote server, when the squad declares one.</param>
public sealed partial record McpDeclaration(string Name, string? Url = null)
{
    /// <summary>Rejects declarations the runtime cannot register or pass as a static MCP name.</summary>
    public void Validate(string source)
    {
        if (string.IsNullOrEmpty(Name) || !NamePattern().IsMatch(Name))
        {
            throw new AboxException(
                $"'{source}': MCP server '{Name}' has an 'mcp.name' that is not valid. Use [A-Za-z0-9_][A-Za-z0-9_.-]*."
            );
        }

        if (Url is not null && !IsValidUrl(Url))
        {
            throw new AboxException(
                $"'{source}': MCP server '{Name}' has an 'mcp.url' that is not valid. Use an absolute http or https URL without userinfo."
            );
        }
    }

    private static bool IsValidUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var url)
        && url.Scheme is "http" or "https"
        && string.IsNullOrEmpty(url.UserInfo);

    [GeneratedRegex(@"\A[A-Za-z0-9_][A-Za-z0-9_.-]*\z")]
    private static partial Regex NamePattern();
}
