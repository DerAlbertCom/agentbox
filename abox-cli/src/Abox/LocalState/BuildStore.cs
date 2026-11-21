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
    public string DirectoryFor(SquadBuildIdentity identity)
    {
        identity.Validate();
        return paths.BuildDirectory(identity);
    }

    /// <summary>The manifest of a build, or null when there is no such build.</summary>
    public BuildManifest? Load(SquadBuildIdentity identity)
    {
        var path = Path.Combine(DirectoryFor(identity), ManifestFileName);
        // A present qualified manifest is authoritative, including when it is invalid.
        var manifest = File.Exists(path) ? Read(path, identity) : ReadLegacy(identity);
        if (manifest is not null)
        {
            EnsureOwnership(manifest, identity);
        }
        return manifest;
    }

    /// <summary>Recovers hint identity only from a legacy manifest's explicit ownership marker.</summary>
    public BuildManifest? LoadLegacy(string squad, string tag)
    {
        var unknownOwner = new SquadBuildIdentity("<name>", squad, tag);
        var manifest = ReadLegacy(unknownOwner);
        if (manifest is null)
        {
            return null;
        }
        if (
            !ImageName.TryParseTeam(manifest.Reference, tag, out var team)
            || !RegistryImageName.IsValidRepository(
                SquadBuildIdentity.TeamRepository(team).ToLowerInvariant()
            )
        )
        {
            throw Rebuild(unknownOwner);
        }
        EnsureOwnership(manifest, new SquadBuildIdentity(team, squad, tag));
        return manifest;
    }

    private BuildManifest? ReadLegacy(SquadBuildIdentity identity)
    {
        (identity with { Team = "legacy" }).Validate();
        var path = Path.Combine(
            paths.BuildsDirectory,
            identity.Squad,
            identity.Tag,
            ManifestFileName
        );
        if (!File.Exists(path))
        {
            return null;
        }
        return Read(path, identity);
    }

    private static BuildManifest Read(string path, SquadBuildIdentity identity)
    {
        try
        {
            return JsonSerializer.Deserialize<BuildManifest>(
                    File.ReadAllText(path),
                    SerializerOptions
                ) ?? throw Rebuild(identity);
        }
        catch (Exception exception)
            when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            throw new AboxException(
                $"The build '{identity.Squad}:{identity.Tag}' is not readable: {exception.Message} {Rebuild(identity).Message}",
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
            throw Rebuild(new SquadBuildIdentity("<name>", manifest.Squad, manifest.Tag));
        }
        var directory = DirectoryFor(new SquadBuildIdentity(team, manifest.Squad, manifest.Tag));
        Directory.CreateDirectory(directory);

        File.WriteAllText(
            Path.Combine(directory, ManifestFileName),
            JsonSerializer.Serialize(manifest, SerializerOptions)
        );
    }

    private static void EnsureOwnership(BuildManifest manifest, SquadBuildIdentity identity)
    {
        if (
            !ImageName.TryParseTeam(manifest.Reference, identity.Tag, out var owner)
            || !owner.Equals(identity.Team, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(manifest.Squad, identity.Squad, StringComparison.OrdinalIgnoreCase)
            || manifest.Tag != identity.Tag
        )
        {
            throw Rebuild(identity);
        }
    }

    private static AboxException Rebuild(SquadBuildIdentity identity) =>
        new(
            $"The local build '{identity.Squad}:{identity.Tag}' has no proven ownership for team '{identity.Team}'. Prepare it again: abox build --team {identity.Team} --tag {identity.Tag}"
        );
}
