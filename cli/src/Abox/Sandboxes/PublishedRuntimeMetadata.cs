using System.Text.Json;
using System.Text.RegularExpressions;
using Abox.Kits;
using Abox.LocalState;

namespace Abox.Sandboxes;

/// <summary>Decodes authoritative published runtime settings, including legacy schema compatibility.</summary>
internal static class PublishedRuntimeMetadata
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        RespectNullableAnnotations = true,
    };

    internal sealed record Identity(string Team, string Squad, string Tag);

    internal static BuildManifest Read(string json, Identity expected, JsonElement descriptor)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        ValidateIdentity(root, expected);
        var state =
            root.GetProperty("State").Deserialize<List<ManifestState>>(JsonOptions)
            ?? throw new JsonException("Missing State.");
        var mcp =
            root.GetProperty("Mcp").Deserialize<List<ManifestMcp>>(JsonOptions)
            ?? throw new JsonException("Missing Mcp.");
        var createdAt = root.GetProperty("CreatedAt").GetDateTimeOffset();
        if (
            createdAt == default
            || state.Any(item => item is null)
            || mcp.Any(item => item is null)
        )
            throw new JsonException("Incomplete runtime metadata.");
        ValidateState(state, descriptor);
        ValidateMcp(mcp);
        return new BuildManifest
        {
            Squad = expected.Squad,
            Tag = expected.Tag,
            CreatedAt = createdAt,
            State = state,
            Mcp = mcp,
        };
    }

    private static void ValidateIdentity(JsonElement root, Identity expected)
    {
        // The original publisher omitted SchemaVersion; its otherwise identical format is version 1.
        if (root.TryGetProperty("SchemaVersion", out var version) && version.GetInt32() != 1)
            throw new JsonException("Squad metadata schema mismatch.");
        if (
            !string.Equals(
                root.GetProperty("Team").GetString(),
                expected.Team,
                StringComparison.OrdinalIgnoreCase
            )
            || !string.Equals(
                root.GetProperty("Squad").GetString(),
                expected.Squad,
                StringComparison.OrdinalIgnoreCase
            )
            || root.GetProperty("Tag").GetString() != expected.Tag
        )
            throw new JsonException("Squad metadata identity mismatch.");
    }

    private static void ValidateState(IReadOnlyList<ManifestState> state, JsonElement descriptor)
    {
        try
        {
            StateEntry.EnsureDisjoint(
                state
                    .Select(entry => StateEntry.FromStored(entry.SandboxPath, entry.RelativePath))
                    .ToList()
            );
        }
        catch (AboxException exception)
        {
            throw new JsonException("Unsafe or overlapping state paths.", exception);
        }

        foreach (var entry in state)
        {
            if (!HasHostMount(descriptor, entry.SandboxPath))
                throw new JsonException("Unsafe state path or missing host-mount declaration.");
        }
    }

    private static bool HasHostMount(JsonElement descriptor, string path) =>
        descriptor
            .GetProperty("capabilities")
            .EnumerateArray()
            .Any(capability =>
                capability.GetProperty("type").GetString() == "com.docker.sandbox/host-mount@1"
                && capability.GetProperty("config").GetProperty("path").GetString() == path
            );

    private static void ValidateMcp(IReadOnlyList<ManifestMcp> mcp)
    {
        if (mcp.DistinctBy(item => item.Name).Count() != mcp.Count)
            throw new JsonException("Duplicate MCP declarations.");
        foreach (var server in mcp)
        {
            if (!Regex.IsMatch(server.Name, @"\A[A-Za-z0-9_][A-Za-z0-9_.-]*\z"))
                throw new JsonException("Invalid MCP name.");
            if (server.Url is not null && !IsMcpUrl(server.Url))
                throw new JsonException("Invalid MCP URL.");
        }
    }

    private static bool IsMcpUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var url)
        && url.Scheme is "http" or "https"
        && string.IsNullOrEmpty(url.UserInfo);
}
