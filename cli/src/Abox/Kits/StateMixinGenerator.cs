using System.Text;

namespace Abox.Kits;

/// <summary>
/// Writes the mixin that declares the host directories the sandbox keeps. sbx mounts a
/// host directory only where a kit asks for it, so abox writes the ask itself.
/// </summary>
public sealed class StateMixinGenerator
{
    /// <summary>The name of the generated mixin, without the extension.</summary>
    public const string MixinName = "state";

    /// <summary>
    /// Writes the mixin into a directory, replacing what was there before, and returns the
    /// directory it was written to.
    /// </summary>
    public string Generate(IReadOnlyList<StateEntry> entries, string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);

        var descriptor = new StringBuilder()
            .AppendLine("# syntax=docker/sandbox-kit:3")
            .AppendLine("schemaVersion: \"3\"")
            .AppendLine("kind: mixin")
            .AppendLine("displayName: abox state")
            .AppendLine("description: The directories abox keeps when the sandbox is replaced.")
            .AppendLine("capabilities:");

        foreach (var entry in entries)
        {
            descriptor
                .AppendLine("  - type: com.docker.sandbox/host-mount@1")
                .AppendLine("    config:")
                .AppendLine($"      path: {entry.SandboxPath}");
        }

        File.WriteAllText(Path.Combine(targetDirectory, MixinName + ".yaml"), descriptor.ToString());

        return targetDirectory;
    }
}
