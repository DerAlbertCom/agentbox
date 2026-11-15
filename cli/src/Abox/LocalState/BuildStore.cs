using System.Text.Json;
using Abox.Configuration;
using Abox.Naming;

namespace Abox.LocalState;

/// <summary>
/// Keeps the local builds: one directory per team, squad and tag, each with the manifest that
/// says what the build holds.
/// </summary>
public sealed class BuildStore(AboxPaths paths)
{
    private const string ManifestFileName = "build.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        AllowDuplicateProperties = false,
    };

    /// <summary>The directory of one build.</summary>
    public string DirectoryFor(string team, string squad, string tag)
    {
        RegistryImageName.ValidateTag(tag);
        RegistryImageName.SquadRepository(team, squad);
        return paths.BuildDirectory(team, squad, tag);
    }

    /// <summary>The manifest of a build, or null when there is no such build.</summary>
    public BuildManifest? Load(string team, string squad, string tag)
    {
        var path = Path.Combine(DirectoryFor(team, squad, tag), ManifestFileName);
        var manifest = File.Exists(path)
            ? Read(path, team, squad, tag)
            : ReadLegacy(squad, tag, team);
        if (manifest is not null)
        {
            EnsureOwnership(manifest, team, squad, tag);
        }
        return manifest;
    }

    /// <summary>Recovers hint identity only from a legacy manifest's explicit ownership marker.</summary>
    public BuildManifest? LoadLegacy(string squad, string tag)
    {
        var manifest = ReadLegacy(squad, tag);
        if (manifest is null)
        {
            return null;
        }
        if (
            !ImageName.TryParseTeam(manifest.Reference, tag, out var team)
            || !RegistryImageName.IsValidRepository(
                $"agentbox-{ImageName.Tenant}-{team}".ToLowerInvariant()
            )
        )
        {
            throw Rebuild(squad, tag, "<name>");
        }
        EnsureOwnership(manifest, team, squad, tag);
        return manifest;
    }

    private BuildManifest? ReadLegacy(string squad, string tag, string team = "<name>")
    {
        RegistryImageName.ValidateTag(tag);
        RegistryImageName.SquadRepository("legacy", squad);
        var path = Path.Combine(paths.BuildsDirectory, squad, tag, ManifestFileName);
        if (!File.Exists(path))
        {
            return null;
        }
        return Read(path, team, squad, tag);
    }

    private static BuildManifest Read(string path, string team, string squad, string tag)
    {
        try
        {
            return JsonSerializer.Deserialize<BuildManifest>(
                    File.ReadAllText(path),
                    SerializerOptions
                ) ?? throw Rebuild(squad, tag, team);
        }
        catch (Exception exception)
            when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            throw new AboxException(
                $"The build '{squad}:{tag}' is not readable: {exception.Message} {Rebuild(squad, tag, team).Message}",
                exception
            );
        }
    }

    /// <summary>Writes the manifest of a build.</summary>
    public void Save(BuildManifest manifest)
    {
        RegistryImageName.ValidateTag(manifest.Tag);
        if (!ImageName.TryParseTeam(manifest.Reference, manifest.Tag, out var team))
        {
            throw Rebuild(manifest.Squad, manifest.Tag, "<name>");
        }
        var directory = DirectoryFor(team, manifest.Squad, manifest.Tag);
        Directory.CreateDirectory(directory);

        File.WriteAllText(
            Path.Combine(directory, ManifestFileName),
            JsonSerializer.Serialize(manifest, SerializerOptions)
        );
    }

    private static void EnsureOwnership(
        BuildManifest manifest,
        string team,
        string squad,
        string tag
    )
    {
        if (
            !ImageName.TryParseTeam(manifest.Reference, tag, out var owner)
            || !owner.Equals(team, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(manifest.Squad, squad, StringComparison.OrdinalIgnoreCase)
            || manifest.Tag != tag
        )
        {
            throw Rebuild(squad, tag, team);
        }
    }

    private static AboxException Rebuild(string squad, string tag, string team) =>
        new(
            $"The local build '{squad}:{tag}' has no proven ownership for team '{team}'. Prepare it again: abox build --team {team} --tag {tag}"
        );
}
