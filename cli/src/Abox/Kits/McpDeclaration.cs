namespace Abox.Kits;

/// <summary>
/// An MCP server a squad uses.
/// </summary>
/// <param name="Name">The server name, as sbx knows it.</param>
/// <param name="Url">The endpoint of a remote server, when the squad declares one.</param>
public sealed record McpDeclaration(string Name, string? Url = null);
