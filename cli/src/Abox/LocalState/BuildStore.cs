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
    public string DirectoryFor(string team, string squad, string tag) =>
        DirectoryFor(ValidatedIdentity(team, squad, tag));

    private static BuildIdentity ValidatedIdentity(string team, string squad, string tag)
    {
        RegistryImageName.ValidateTag(tag);
        RegistryImageName.SquadRepository(team, squad);
        return new BuildIdentity(team, squad, tag);
    }

    private string DirectoryFor(BuildIdentity identity) =>
        paths.BuildDirectory(identity.Team, identity.Squad, identity.Tag);

    /// <summary>The manifest of a build, or null when there is no such build.</summary>
    public BuildManifest? Load(string team, string squad, string tag)
    {
        var identity = ValidatedIdentity(team, squad, tag);
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
        var unknownOwner = new BuildIdentity("<name>", squad, tag);
        var manifest = ReadLegacy(unknownOwner);
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
            throw unknownOwner.Rebuild();
        }
        EnsureOwnership(manifest, new BuildIdentity(team, squad, tag));
        return manifest;
    }

    private BuildManifest? ReadLegacy(BuildIdentity identity)
    {
        RegistryImageName.ValidateTag(identity.Tag);
        RegistryImageName.SquadRepository("legacy", identity.Squad);
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

    private static BuildManifest Read(string path, BuildIdentity identity)
    {
        try
        {
            return JsonSerializer.Deserialize<BuildManifest>(
                    File.ReadAllText(path),
                    SerializerOptions
                ) ?? throw identity.Rebuild();
        }
        catch (Exception exception)
            when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            throw new AboxException(
                $"The build '{identity.Squad}:{identity.Tag}' is not readable: {exception.Message} {identity.Rebuild().Message}",
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
            throw new BuildIdentity("<name>", manifest.Squad, manifest.Tag).Rebuild();
        }
        var directory = DirectoryFor(team, manifest.Squad, manifest.Tag);
        Directory.CreateDirectory(directory);

        File.WriteAllText(
            Path.Combine(directory, ManifestFileName),
            JsonSerializer.Serialize(manifest, SerializerOptions)
        );
    }

    private static void EnsureOwnership(BuildManifest manifest, BuildIdentity identity)
    {
        if (
            !ImageName.TryParseTeam(manifest.Reference, identity.Tag, out var owner)
            || !owner.Equals(identity.Team, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(manifest.Squad, identity.Squad, StringComparison.OrdinalIgnoreCase)
            || manifest.Tag != identity.Tag
        )
        {
            throw identity.Rebuild();
        }
    }

    private sealed record BuildIdentity(string Team, string Squad, string Tag)
    {
        public AboxException Rebuild() =>
            new(
                $"The local build '{Squad}:{Tag}' has no proven ownership for team '{Team}'. Prepare it again: abox build --team {Team} --tag {Tag}"
            );
    }
}
