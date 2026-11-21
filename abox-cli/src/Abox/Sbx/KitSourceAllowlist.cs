using System.Text.Json;

namespace Abox.Sbx;

// The evaluated kit.allowedSources record, not a general sbx settings schema.
internal sealed record KitSourceAllowlist(string[] Sources, string Source)
{
    internal const string Key = "kit.allowedSources";

    internal static KitSourceAllowlist DecodeRecord(JsonElement record)
    {
        if (record.ValueKind != JsonValueKind.Object)
            throw InvalidRecord();
        if (RequiredText(record, "key") != Key || RequiredText(record, "type") != "json")
            throw InvalidRecord();

        var source = RequiredText(record, "source");
        if (!record.TryGetProperty("value", out var value))
            throw InvalidRecord();
        return new KitSourceAllowlist(DecodeSourceArray(value), source);
    }

    private static string RequiredText(JsonElement record, string property)
    {
        if (!record.TryGetProperty(property, out var value) || !IsNonemptyText(value))
            throw InvalidRecord();
        return value.GetString()!;
    }

    private static string[] DecodeSourceArray(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array)
            throw InvalidRecord();

        var sources = new List<string>();
        foreach (var entry in value.EnumerateArray())
        {
            if (!IsNonemptyText(entry))
                throw InvalidRecord();
            // Preserve spelling, order and duplicates; this is validation, not normalization.
            sources.Add(entry.GetString()!);
        }
        return sources.ToArray();
    }

    private static bool IsNonemptyText(JsonElement value) =>
        value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString());

    private static AboxException InvalidRecord() =>
        new(
            $"Cannot read '{Key}': expected a settings record with a JSON array of nonempty source prefixes. No sandbox was replaced."
        );
}
