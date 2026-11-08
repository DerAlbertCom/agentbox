namespace Abox.Kits;

/// <summary>
/// An MCP server a squad uses.
/// </summary>
/// <param name="Name">The server name, as sbx knows it.</param>
/// <param name="Url">The endpoint of a remote server, when the squad declares one.</param>
/// <param name="Command">The command of a local server, when the squad declares one.</param>
/// <param name="Arguments">The arguments of a local server.</param>
/// <param name="Headers">The headers of a remote server.</param>
public sealed record McpDeclaration(
    string Name,
    string? Url = null,
    string? Command = null,
    IReadOnlyList<string>? Arguments = null,
    IReadOnlyDictionary<string, string>? Headers = null
);
