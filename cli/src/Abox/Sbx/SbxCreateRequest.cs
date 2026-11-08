namespace Abox.Sbx;

/// <summary>Everything <c>sbx create</c> needs.</summary>
/// <param name="KitSource">The workload kit: a local kit directory.</param>
/// <param name="Location">The folder the sandbox works on.</param>
/// <param name="Name">The sandbox name.</param>
/// <param name="Mixins">The mixin kits, as local directories or registry references.</param>
/// <param name="KitArguments">The values for the kits' arguments.</param>
/// <param name="Mounts">The host directories mounted into the sandbox.</param>
/// <param name="StaticMcpServers">The MCP servers the sandbox always carries.</param>
public sealed record SbxCreateRequest(
    string KitSource,
    string Location,
    string Name,
    IReadOnlyList<string> Mixins,
    IReadOnlyDictionary<string, string> KitArguments,
    IReadOnlyList<SbxMount> Mounts,
    IReadOnlyList<string> StaticMcpServers);
