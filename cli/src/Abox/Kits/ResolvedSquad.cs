namespace Abox.Kits;

/// <summary>
/// A squad with its whole configuration chain resolved: one workload, its kits, its
/// MCP servers and its state.
/// </summary>
public sealed class ResolvedSquad(
    string name,
    string rootDirectory,
    WorkloadFile workload,
    IReadOnlyList<KitSource> kits,
    IReadOnlyList<McpDeclaration> mcp,
    IReadOnlyList<StateEntry> state,
    IReadOnlyList<string> configFiles,
    IReadOnlyList<SecretDeclaration> secrets
)
{
    /// <summary>The name of the squad.</summary>
    public string Name { get; } = name;

    /// <summary>The owning project root discovered with the selected team.</summary>
    public string RootDirectory { get; } = rootDirectory;

    /// <summary>The workload the base image is built from.</summary>
    public WorkloadFile Workload { get; } = workload;

    /// <summary>The kits of the squad.</summary>
    public IReadOnlyList<KitSource> Kits { get; } = kits;

    /// <summary>The MCP servers of the squad.</summary>
    public IReadOnlyList<McpDeclaration> Mcp { get; } = mcp;

    /// <summary>The directories that survive a replacement of the sandbox.</summary>
    public IReadOnlyList<StateEntry> State { get; } = state;

    /// <summary>The configuration files the squad is made of.</summary>
    public IReadOnlyList<string> ConfigFiles { get; } = configFiles;

    /// <summary>The secrets of the squad: how the values of the services are obtained.</summary>
    public IReadOnlyList<SecretDeclaration> Secrets { get; } = secrets;
}
