using System.Text;
using System.Text.Json;
using Abox.Configuration;

namespace Abox.UserSecrets;

internal sealed class EffectiveAppSettings(
    string root,
    string project,
    UserSecretSelection selection
)
{
    private readonly string relativeProject = Path.GetRelativePath(root, project);
    private readonly string directory = Path.GetDirectoryName(project)!;

    internal async Task<ProjectSettings> ReadAsync(CancellationToken cancellationToken)
    {
        var merged = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        var hasSource = false;
        foreach (var name in new[] { "appsettings.json", "appsettings.development.json" })
        {
            var snapshot = await ReadSourceAsync(name, cancellationToken);
            if (snapshot is null)
                continue;
            hasSource = true;
            foreach (var (key, value) in snapshot.EffectiveLeaves)
            {
                // Retain the winning source's spelling as well as its value.
                merged.Remove(key);
                merged.Add(key, value);
            }
        }
        var allSettings = UserSecretSnapshot.ParseAll(
            selection.Id,
            JsonSerializer.Serialize(merged)
        );
        return new ProjectSettings(
            relativeProject,
            allSettings.SelectKeys(selection),
            allSettings.Values,
            hasSource
        );
    }

    private async Task<UserSecretSnapshot?> ReadSourceAsync(
        string name,
        CancellationToken cancellationToken
    )
    {
        try
        {
            // Use actual spelling even on case-insensitive hosts: development uses lowercase.
            var path = Directory
                .EnumerateFiles(directory)
                .FirstOrDefault(path => Path.GetFileName(path) == name);
            if (path is null)
                return null;
            WorkspaceProjectFiles.EnsureOrdinaryFile(path);
            var json = await File.ReadAllTextAsync(
                path,
                new UTF8Encoding(false, true),
                cancellationToken
            );
            return UserSecretSnapshot.ParseAll(selection.Id, json);
        }
        catch (Exception exception)
            when (exception
                    is AboxException
                        or IOException
                        or UnauthorizedAccessException
                        or DecoderFallbackException
            )
        {
            throw new AboxException(
                $"Project {JsonSerializer.Serialize(relativeProject)}, store '{selection.Id}': cannot read {name} as a UTF-8 JSON object with unique effective .NET configuration keys (case-insensitive). Check file permissions and use an ordinary file, not a file link/reparse point; no stores were copied."
            );
        }
    }
}
