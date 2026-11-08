using System.Text.Json.Serialization;

namespace Abox.Sbx;

// The JSON shapes sbx writes with --json. Only the fields abox reads are here;
// sbx may add more, and older versions may leave some out, so every field is optional.

/// <summary>One sandbox of <c>sbx ls --json</c>.</summary>
internal sealed class SbxSandbox
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; set; }
}

/// <summary>The report of <c>sbx diagnose --json</c>.</summary>
internal sealed class SbxDiagnose
{
    [JsonPropertyName("checks")]
    public List<SbxCheck> Checks { get; set; } = [];
}

/// <summary>One check of <c>sbx diagnose --json</c>.</summary>
internal sealed class SbxCheck
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("hint")]
    public string? Hint { get; set; }
}

/// <summary>The secrets of <c>sbx secret ls --json</c>.</summary>
internal sealed class SbxSecrets
{
    [JsonPropertyName("secrets")]
    public List<SbxSecret> Secrets { get; set; } = [];

    [JsonPropertyName("custom_secrets")]
    public List<SbxCustomSecret> CustomSecrets { get; set; } = [];
}

/// <summary>A secret of a service sbx knows.</summary>
internal sealed class SbxSecret
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

/// <summary>A secret of a service sbx does not know.</summary>
internal sealed class SbxCustomSecret
{
    [JsonPropertyName("targets")]
    public List<string> Targets { get; set; } = [];

    [JsonPropertyName("env")]
    public string? EnvironmentVariable { get; set; }
}

/// <summary>One MCP server of <c>sbx mcp ls --json</c>.</summary>
internal sealed class SbxMcpServer
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}
