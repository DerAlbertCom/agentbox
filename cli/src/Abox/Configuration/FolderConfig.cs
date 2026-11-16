using System.Text;
using System.Text.Json;

namespace Abox.Configuration;

/// <summary>Runtime-only opt-in in the sandbox folder, never a build input.</summary>
public sealed class FolderConfig
{
    private FolderConfig(
        string location,
        string? squad,
        IReadOnlyList<UserSecretSelection> userSecrets,
        int? portOffset
    )
    {
        Location = Path.GetFullPath(location);
        Squad = squad;
        UserSecrets = userSecrets;
        PortOffset = portOffset;
    }

    public string? Squad { get; }
    public IReadOnlyList<UserSecretSelection> UserSecrets { get; }
    public int? PortOffset { get; }
    internal string Location { get; }

    /// <summary>Select from the entry-time snapshot; never reread or write folder configuration.</summary>
    public static int SelectPortOffset(FolderConfig? folderConfig, int? explicitOffset = null)
    {
        var offset = explicitOffset ?? folderConfig?.PortOffset ?? 10000;
        if (offset is < -65534 or > 65534)
            throw new AboxException(
                "Port offset must be an integer in -65534..65534; every resulting host port must also be in 1..65535."
            );
        return offset;
    }

    /// <summary>The folder selects the squad; an explicit matching selection is allowed.</summary>
    public string? SelectSquad(string? explicitSquad)
    {
        if (
            Squad is not null
            && explicitSquad is not null
            && !Squad.Equals(explicitSquad, StringComparison.OrdinalIgnoreCase)
        )
            throw new AboxException(
                "The selected squad conflicts with 'squad' in abox.json. Use the folder's squad or update abox.json before creating or replacing its sandbox."
            );
        return Squad ?? explicitSquad;
    }

    /// <summary>Do not implicitly replace a sandbox whose recorded squad differs or is unknown.</summary>
    public void EnsureSandboxSquad(string? recordedSquad)
    {
        if (Squad is not null && string.IsNullOrWhiteSpace(recordedSquad))
            throw new AboxException(
                "Cannot verify the sandbox's squad against abox.json. Create it with abox create for this folder before replacing it."
            );
        SelectSquad(recordedSquad);
    }

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
                if (
                    !seen.Add(property.Name)
                    || property.Name is not ("squad" or "userSecrets" or "portOffset")
                )
                    throw new AboxException(
                        "abox.json has duplicate or unknown properties. Use only 'squad', 'userSecrets' and 'portOffset'."
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
            int? portOffset = null;
            if (root.TryGetProperty("portOffset", out var offsetElement))
            {
                if (
                    offsetElement.ValueKind != JsonValueKind.Number
                    || !offsetElement.TryGetInt32(out var offset)
                )
                    throw new AboxException(
                        "abox.json: 'portOffset' must be an integer in -65534..65534."
                    );
                portOffset = SelectPortOffset(null, offset);
            }
            return new FolderConfig(location, squad, selections, portOffset);
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
