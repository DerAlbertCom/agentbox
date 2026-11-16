using Abox.Configuration;
using Abox.Kits;
using Abox.LocalState;
using Abox.Naming;

namespace Abox.Features.Run;

/// <summary>Resolves recovery provenance without writing diagnostics while rendering a command.</summary>
internal sealed class CreateRecoveryHints(
    AboxConfig config,
    SquadResolver resolver,
    BuildStore store,
    SandboxRecordStore records
)
{
    public CreateRecoveryHint Resolve(string name, string location, FolderConfig? folderConfig)
    {
        var record = records.Load(name);
        var squad =
            folderConfig?.Squad ?? (string.IsNullOrWhiteSpace(record?.Squad) ? null : record.Squad);
        var tag = string.IsNullOrWhiteSpace(record?.Tag) ? config.DefaultTag : record.Tag;
        var team = string.IsNullOrWhiteSpace(record?.Team) ? null : record.Team;
        var build = LoadBuild(record, squad, tag);
        ImageName.TryParseTeam(build.Manifest?.Reference, tag, out var buildTeam);
        team ??= buildTeam;
        team ??= config.DefaultTeam;
        if (squad is null && team is not null)
        {
            var discovered = DiscoverTeam(location, team);
            team = discovered?.Name ?? team;
            squad = discovered?.Squads.Count == 1 ? discovered.Squads[0] : null;
        }

        var local =
            buildTeam is not null && buildTeam.Equals(team, StringComparison.OrdinalIgnoreCase);
        return new CreateRecoveryHint(
            Render(team, squad, tag, local ? build.Manifest : null, location),
            build.Diagnostic
        );
    }

    private LocalHintBuild LoadBuild(SandboxRecord? record, string? squad, string tag)
    {
        if (record is not { KitReference: null } || squad is null)
        {
            return new(null, null);
        }

        try
        {
            var manifest = string.IsNullOrWhiteSpace(record.Team)
                ? store.LoadLegacy(squad, tag)
                : store.Load(record.Team, squad, tag);
            return new(manifest, null);
        }
        catch (AboxException exception)
        {
            // Failed provenance excludes local flags, but still permits a remote recovery hint.
            return new(null, exception.Message);
        }
    }

    private SquadResolver.ResolvedTeam? DiscoverTeam(string location, string team)
    {
        try
        {
            return resolver.ResolveTeam(location, team);
        }
        catch (AboxException)
        {
            // Discovery is optional: remote creation does not require local team sources.
            return null;
        }
    }

    private static string Render(
        string? team,
        string? squad,
        string tag,
        BuildManifest? localBuild,
        string location
    )
    {
        var placeholders =
            team is null || squad is null ? " (replace <name> placeholders)" : string.Empty;
        var local = localBuild is not null ? " --local" : string.Empty;
        var root = !string.IsNullOrWhiteSpace(localBuild?.RootDirectory)
            ? $" --root \"{localBuild.RootDirectory}\""
            : string.Empty;
        return $"abox create --team {team ?? "<name>"} --squad {squad ?? "<name>"} --tag {tag}{local}{root} --location \"{location}\"{placeholders}";
    }

    private sealed record LocalHintBuild(BuildManifest? Manifest, string? Diagnostic);
}

/// <summary>A rendered recovery instruction and any safe, nonfatal build-provenance diagnostic.</summary>
internal sealed record CreateRecoveryHint(string Command, string? Diagnostic);
