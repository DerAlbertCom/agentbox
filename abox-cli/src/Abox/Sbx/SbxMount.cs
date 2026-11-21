namespace Abox.Sbx;

/// <summary>A host directory that is mounted into the sandbox.</summary>
public sealed record SbxMount(string HostPath, string SandboxPath)
{
    public override string ToString() => $"{HostPath}:{SandboxPath}";
}
