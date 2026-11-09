namespace Abox.Build;

/// <summary>
/// What abox knows about a sandbox it created.
/// </summary>
public sealed class SandboxRecord
{
    /// <summary>The team the sandbox was created from.</summary>
    public string Team { get; set; } = string.Empty;

    /// <summary>The squad the sandbox was created from.</summary>
    public string Squad { get; set; } = string.Empty;

    /// <summary>The tag of the local build the sandbox was created from.</summary>
    public string Tag { get; set; } = string.Empty;

    /// <summary>The published kit set, or null for a sandbox created from a local build.</summary>
    public string? KitReference { get; set; }

    /// <summary>When that build was made.</summary>
    public DateTimeOffset BuildCreatedAt { get; set; }

    /// <summary>When the sandbox was created.</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
