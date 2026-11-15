using System.Text;
using System.Text.Json;
using Abox.UserSecrets;

namespace Abox.Configuration;

/// <summary>Runtime-only opt-in in the sandbox folder, never a build input.</summary>
public sealed class FolderConfig
{
    private FolderConfig(string? squad, IReadOnlyList<UserSecretSelection> userSecrets)
    {
        Squad = squad;
        UserSecrets = userSecrets;
    }

    public string? Squad { get; }
    public IReadOnlyList<UserSecretSelection> UserSecrets { get; }

    /// <summary>Missing configuration is a no-op for lifecycle callers.</summary>
    public static async Task<FolderConfig?> LoadAsync(
        string location,
        CancellationToken cancellationToken
    )
    {
        var path = Path.Combine(location, "abox.json");
        try
        {
            var json = await File.ReadAllTextAsync(
                path,
                new UTF8Encoding(false, true),
                cancellationToken
            );
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new AboxException("abox.json must contain an object.");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in root.EnumerateObject())
                if (!seen.Add(property.Name) || property.Name is not ("squad" or "userSecrets"))
                    throw new AboxException(
                        "abox.json has duplicate or unknown properties. Use only 'squad' and 'userSecrets'."
                    );

            string? squad = null;
            if (root.TryGetProperty("squad", out var squadElement))
            {
                if (
                    squadElement.ValueKind != JsonValueKind.String
                    || string.IsNullOrWhiteSpace(squadElement.GetString())
                )
                    throw new AboxException("abox.json: 'squad' must be a nonempty string.");
                squad = squadElement.GetString();
            }
            var selections = root.TryGetProperty("userSecrets", out var entries)
                ? UserSecretSelection.Parse(entries)
                : [];
            return new FolderConfig(squad, selections);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
        catch (DecoderFallbackException)
        {
            throw new AboxException(
                "abox.json has invalid UTF-8. Fix the folder configuration; no stores were copied."
            );
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            throw new AboxException(
                "abox.json is not valid JSON. Check its structure; no secret values were copied."
            );
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new AboxException(
                "Cannot read abox.json. Check the folder and file permissions."
            );
        }
    }
}
