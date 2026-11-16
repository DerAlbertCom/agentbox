using System.Text.Json;

namespace Abox.Launchers;

// hc 0.1.17's machine.list response and herdr-app 0.1.16's status response.
// Validate the whole relevant shape before interpreting absence or health.
internal static class HerdrResponses
{
    internal static bool ContainsMachine(string json, string name)
    {
        using var document = Parse(json, "hc machine ls --json");
        var machines = RequiredArray(document.RootElement, "machines", "hc machine ls --json");
        var found = false;
        foreach (var machine in machines.EnumerateArray())
        {
            if (
                machine.ValueKind != JsonValueKind.Object
                || !machine.TryGetProperty("name", out var identity)
                || identity.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(identity.GetString())
            )
                throw Invalid("hc machine ls --json", "every machine must have a string name");
            found |= string.Equals(identity.GetString(), name, StringComparison.Ordinal);
        }
        return found;
    }

    internal static void RequireHealthyStatus(string json)
    {
        const string command = "herdr-app status";
        using var document = Parse(json, command);
        var errors = RequiredArray(document.RootElement, "errors", command);
        if (document.RootElement.TryGetProperty("error", out _))
            throw Invalid(command, "the window returned an error");
        foreach (var error in errors.EnumerateArray())
            if (error.ValueKind != JsonValueKind.String)
                throw Invalid(command, "'errors' must contain only strings");
        if (errors.GetArrayLength() > 0)
            throw new AboxException(
                "'herdr-app status' reports app errors. Inspect 'herdr-app status' and fix the app configuration before retrying. No app or core was restarted."
            );
    }

    private static JsonDocument Parse(string json, string command)
    {
        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            throw Invalid(command, "expected a JSON object");
        }
    }

    private static JsonElement RequiredArray(JsonElement root, string property, string command)
    {
        if (
            root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty(property, out var value)
            || value.ValueKind != JsonValueKind.Array
        )
            throw Invalid(command, $"expected an object with a '{property}' array");
        return value;
    }

    private static AboxException Invalid(string command, string reason) =>
        new(
            $"Invalid response from '{command}': {reason}. Check the installed Herdr version and run '{command}' before retrying. Herdr was not opened."
        );
}
