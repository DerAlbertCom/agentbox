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

    /// <summary>The JFrog repository used to pull published squad kit sets.</summary>
    public string PullRepository { get; init; } = "mtiae-docker-release";

    /// <summary>Case-insensitive literal substrings forbidden in selected original user-secret values.</summary>
    public IReadOnlyList<string> UserSecretsDenyList { get; init; } = [];

    /// <summary>The absolute sandbox folder, preferring the command line over the user default.</summary>
    public string ResolveLocation(string? location) =>
        Path.GetFullPath(location ?? DefaultLocation ?? Directory.GetCurrentDirectory());

    /// <summary>The explicitly selected team, otherwise the required user default.</summary>
    public string ResolveTeam(string? team) =>
        team
        ?? DefaultTeam
        ?? throw new AboxException(
            "No team is given. Give --team <name> or set 'defaultTeam' in the user configuration."
        );

    /// <summary>Multi-team commands retain all explicit selections, otherwise the user default.</summary>
    public IReadOnlyList<string> ResolveTeams(IReadOnlyList<string>? teams) =>
        teams is { Count: > 0 } ? teams : [ResolveTeam(null)];
}
