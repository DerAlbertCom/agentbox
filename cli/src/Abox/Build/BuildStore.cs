using System.Text.Json;
using Abox.Configuration;

namespace Abox.Build;

/// <summary>
/// Keeps the local builds: one directory per squad and tag, each with the manifest that
/// says what the build holds.
/// </summary>
public sealed class BuildStore
{
    private const string ManifestFileName = "build.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    private readonly AboxPaths _paths;

    public BuildStore(AboxPaths paths) => _paths = paths;

    /// <summary>The directory of one build.</summary>
    public string DirectoryFor(string squad, string tag) => _paths.BuildDirectory(squad, tag);

    /// <summary>The manifest of a build, or null when there is no such build.</summary>
    public BuildManifest? Load(string squad, string tag)
    {
        var path = Path.Combine(DirectoryFor(squad, tag), ManifestFileName);
        if (!File.Exists(path))
        {
            return null;
        }

        var manifest = JsonSerializer.Deserialize<BuildManifest>(
            File.ReadAllText(path),
            SerializerOptions
        );
        return manifest ?? throw new AboxException($"The build '{squad}:{tag}' is not readable.");
    }

    /// <summary>Writes the manifest of a build.</summary>
    public void Save(BuildManifest manifest)
    {
        var directory = DirectoryFor(manifest.Squad, manifest.Tag);
        Directory.CreateDirectory(directory);

        File.WriteAllText(
            Path.Combine(directory, ManifestFileName),
            JsonSerializer.Serialize(manifest, SerializerOptions)
        );
    }
}
