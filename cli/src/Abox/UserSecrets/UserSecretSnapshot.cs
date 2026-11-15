using System.Text.Json;

namespace Abox.UserSecrets;

// Payload-bearing types stay inside the shared module, not public records or diagnostics.
internal sealed class UserSecretSnapshot
{
    internal UserSecretSnapshot(
        string id,
        JsonElement root,
        IReadOnlyDictionary<string, string?> values
    )
    {
        Id = id;
        Root = root.Clone();
        Values = values;
    }

    internal string Id { get; }
    internal JsonElement Root { get; }
    internal IReadOnlyDictionary<string, string?> Values { get; }
    internal int KeyCount => Values.Count;

    internal static UserSecretSnapshot Parse(UserSecretSelection selection, string json)
    {
        var id = selection.Id;
        try
        {
            using var document = JsonDocument.Parse(
                json,
                new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true,
                }
            );
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new AboxException($"User-secret store '{id}' must contain a JSON object.");
            var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            ReadValues(document.RootElement, null, values, id);
            return new UserSecretSnapshot(id, document.RootElement, values);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            throw new AboxException(
                $"User-secret store '{id}' is not valid JSON. Check the host file; no stores were copied."
            );
        }
    }

    private static void ReadValues(
        JsonElement element,
        string? path,
        Dictionary<string, string?> values,
        string id
    )
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object when element.EnumerateObject().Any():
                foreach (var property in element.EnumerateObject())
                    ReadValues(
                        property.Value,
                        path is null ? property.Name : path + ":" + property.Name,
                        values,
                        id
                    );
                return;
            case JsonValueKind.Array when element.GetArrayLength() > 0:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                    ReadValues(item, path + ":" + index++, values, id);
                return;
            default:
                if (path is null)
                    return;
                var value = element.ValueKind switch
                {
                    JsonValueKind.String => element.GetString(),
                    JsonValueKind.Null or JsonValueKind.Object or JsonValueKind.Array => null,
                    _ => element.GetRawText(),
                };
                if (!values.TryAdd(path, value))
                    throw new AboxException(
                        $"User-secret store '{id}' has duplicate effective .NET configuration keys (case-insensitive). No stores were copied."
                    );
                return;
        }
    }
}
