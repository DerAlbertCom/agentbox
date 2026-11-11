namespace Abox.LocalState;

/// <summary>What a local build holds.</summary>
public sealed class BuildManifest
{
    /// <summary>The squad the build belongs to.</summary>
    public string Squad { get; set; } = string.Empty;

    /// <summary>The tag the build is stored under.</summary>
    public string Tag { get; set; } = string.Empty;

    /// <summary>When the build was made.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>The directory the squad's configurations live in.</summary>
    public string RootDirectory { get; set; } = string.Empty;

    /// <summary>The name of the workload the base image is built from.</summary>
    public string WorkloadName { get; set; } = string.Empty;

    /// <summary>The JFrog repository selected by the resolved workload.</summary>
    public string? PublishRepository { get; set; }

    /// <summary>The directory of the generated workload kit.</summary>
    public string WorkloadKitDirectory { get; set; } = string.Empty;

    /// <summary>The prebuilt local content and its thin v3 runtime kit; null on metadata-only or older manifests.</summary>
    public LocalWorkloadImage? LocalWorkload { get; set; }

    /// <summary>The directory of the generated mixin that declares the state directories.</summary>
    public string? StateMixinDirectory { get; set; }

    /// <summary>The image the workload builds on.</summary>
    public string Template { get; set; } = string.Empty;

    /// <summary>The kits of the squad.</summary>
    public List<ManifestKit> Kits { get; set; } = [];

    /// <summary>The MCP servers of the squad.</summary>
    public List<ManifestMcp> Mcp { get; set; } = [];

    /// <summary>The directories the squad keeps across a replacement.</summary>
    public List<ManifestState> State { get; set; } = [];

    /// <summary>The configuration files the squad is made of.</summary>
    public List<string> ConfigFiles { get; set; } = [];

    /// <summary>The unqualified team ownership marker; remote publication adds a squad path segment.</summary>
    public string? Reference { get; set; }

    /// <summary>Whether personal configuration was merged; null means an older, unproven build.</summary>
    public bool? IncludesPersonalConfigurations { get; set; }

    /// <summary>SHA-256 fingerprints of the local source directories and configuration files.</summary>
    public Dictionary<string, string> SourceHashes { get; set; } = [];
}

/// <summary>A unique local tag guarded by its immutable Docker content ID.</summary>
public sealed class LocalWorkloadImage
{
    public string Reference { get; set; } = string.Empty;
    public string ImageId { get; set; } = string.Empty;
    public string DockerContext { get; set; } = string.Empty;
    public string RuntimeKitDirectory { get; set; } = string.Empty;
    public string RuntimeKitHash { get; set; } = string.Empty;
}

/// <summary>A kit of a local build.</summary>
public sealed class ManifestKit
{
    /// <summary>The name the configuration gave the kit.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>What sbx is given: a directory or a reference.</summary>
    public string Reference { get; set; } = string.Empty;

    /// <summary>Whether the kit is built from a local directory.</summary>
    public bool IsLocal { get; set; }

    /// <summary>The values for the kit's arguments.</summary>
    public Dictionary<string, string> Arguments { get; set; } = [];
}

/// <summary>An MCP server of a local build.</summary>
public sealed class ManifestMcp
{
    /// <summary>The name of the server.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The endpoint of the server, when the squad declares one.</summary>
    public string? Url { get; set; }
}

/// <summary>A state directory of a local build.</summary>
public sealed class ManifestState
{
    /// <summary>Where the directory is in the sandbox.</summary>
    public string SandboxPath { get; set; } = string.Empty;

    /// <summary>The path relative to the sandbox home.</summary>
    public string RelativePath { get; set; } = string.Empty;
}
