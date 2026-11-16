using System.Text.Json.Serialization;

namespace Abox.Features.Publish;

/// <summary>A component in composition order. Only supplied kits carry args, even when empty.</summary>
internal sealed record PublicationComponent(
    [property: JsonPropertyName("ref")] string Reference,
    [property: JsonPropertyName("args")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyDictionary<string, string>? Arguments = null
);

/// <summary>The v3 set input; the frontend publishes it as a processed workload.</summary>
internal sealed record PublicationSetDescriptor
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion => "3";

    [JsonPropertyName("kind")]
    public string Kind => "set";

    [JsonPropertyName("displayName")]
    public required string DisplayName { get; init; }

    [JsonPropertyName("kits")]
    public required IReadOnlyList<PublicationComponent> Kits { get; init; }
}

internal sealed record PublicationConfigurationDescriptor
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion => "3";

    [JsonPropertyName("kind")]
    public string Kind => "mixin";

    [JsonPropertyName("build")]
    public string Build => "FROM scratch\nCOPY squad.json /usr/share/abox/squad.json\n";
}

/// <summary>The public squad.json allowlist, deliberately independent of local build provenance.</summary>
internal sealed record PublicationSquadMetadata
{
    public int SchemaVersion => 1;
    public required string Team { get; init; }
    public required string Squad { get; init; }
    public required string Tag { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required IReadOnlyList<PublicationMcp> Mcp { get; init; }
    public required IReadOnlyList<PublicationState> State { get; init; }
    public required IReadOnlyList<PublicationKitMetadata> Kits { get; init; }
}

internal sealed record PublicationMcp(string Name, string? Url);

internal sealed record PublicationState(string SandboxPath, string RelativePath);

internal sealed record PublicationKitMetadata(
    string Name,
    IReadOnlyDictionary<string, string> Arguments
);
