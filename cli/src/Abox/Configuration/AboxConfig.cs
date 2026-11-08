namespace Abox.Configuration;

/// <summary>
/// The settings abox keeps for the user.
/// </summary>
public sealed class AboxConfig
{
    /// <summary>The squad used when the command line does not name one.</summary>
    public string DefaultSquad { get; init; } = "default-squad";

    /// <summary>The folder used when the command line does not name one. Null means the current folder.</summary>
    public string? DefaultLocation { get; init; }

    /// <summary>The directory, relative to a configuration's own directory, that holds kits and workloads.</summary>
    public string KitsDirectory { get; init; } = "kits";

    /// <summary>The tag a local build is stored under when no tag is given.</summary>
    public string DefaultTag { get; init; } = "latest";
}
