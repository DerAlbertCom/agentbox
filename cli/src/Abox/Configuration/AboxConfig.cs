namespace Abox.Configuration;

/// <summary>
/// The settings abox keeps for the user.
/// </summary>
public sealed class AboxConfig
{
    /// <summary>The team used when the command line does not name one. Null means none.</summary>
    public string? DefaultTeam { get; init; }

    /// <summary>The folder used when the command line does not name one. Null means the current folder.</summary>
    public string? DefaultLocation { get; init; }

    /// <summary>The directory of a project that holds a folder per team.</summary>
    public string TeamsDirectory { get; init; } = "teams";

    /// <summary>The directory, relative to the project root, that holds the kits.</summary>
    public string KitsDirectory { get; init; } = "kits";

    /// <summary>The tag a local build is stored under when no tag is given.</summary>
    public string DefaultTag { get; init; } = "latest";
}
