namespace Abox.Sbx;

/// <summary>A TCP application listener published on host IPv4 loopback.</summary>
public sealed record ApplicationPortMapping(int SandboxPort, int HostPort);
