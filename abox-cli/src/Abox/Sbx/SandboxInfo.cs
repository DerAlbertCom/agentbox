namespace Abox.Sbx;

/// <summary>A sandbox as sbx reports it.</summary>
/// <param name="Name">The sandbox name.</param>
/// <param name="Status">The sandbox status, for example "running" or "stopped".</param>
/// <param name="CreatedAt">When the sandbox was created, when sbx reports it.</param>
public sealed record SandboxInfo(string Name, string Status, DateTimeOffset? CreatedAt)
{
    /// <summary>Host workspace paths in runtime order; the first is the primary workspace.</summary>
    public IReadOnlyList<string> Workspaces { get; init; } = [];

    public bool IsRunning => Status.Equals("running", StringComparison.OrdinalIgnoreCase);
}
