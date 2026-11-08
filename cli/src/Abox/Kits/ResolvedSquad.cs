namespace Abox.Kits;

/// <summary>
/// A squad with its whole configuration chain resolved: one workload, its kits, its
/// parameters, its MCP servers and its state.
/// </summary>
public sealed class ResolvedSquad
{
    public ResolvedSquad(
        string name,
        string rootDirectory,
        WorkloadFile workload,
        IReadOnlyList<KitSource> kits,
        IReadOnlyDictionary<string, string> parameters,
        IReadOnlyList<McpDeclaration> mcp,
        IReadOnlyList<StateEntry> state,
        IReadOnlyList<string> configFiles,
        IReadOnlyList<SecretDeclaration> secrets
    )
    {
        Name = name;
        RootDirectory = rootDirectory;
        Workload = workload;
        Kits = kits;
        Parameters = parameters;
        Mcp = mcp;
        State = state;
        ConfigFiles = configFiles;
        Secrets = secrets;
    }

    /// <summary>The name of the squad.</summary>
    public string Name { get; }

    /// <summary>The directory the squad's configuration lives in.</summary>
    public string RootDirectory { get; }

    /// <summary>The workload the base image is built from.</summary>
    public WorkloadFile Workload { get; }

    /// <summary>The kits of the squad.</summary>
    public IReadOnlyList<KitSource> Kits { get; }

    /// <summary>The named values the squad declares.</summary>
    public IReadOnlyDictionary<string, string> Parameters { get; }

    /// <summary>The MCP servers of the squad.</summary>
    public IReadOnlyList<McpDeclaration> Mcp { get; }

    /// <summary>The directories that survive a replacement of the sandbox.</summary>
    public IReadOnlyList<StateEntry> State { get; }

    /// <summary>The configuration files the squad is made of.</summary>
    public IReadOnlyList<string> ConfigFiles { get; }

    /// <summary>The secrets of the squad: how the values of the services are obtained.</summary>
    public IReadOnlyList<SecretDeclaration> Secrets { get; }
}
