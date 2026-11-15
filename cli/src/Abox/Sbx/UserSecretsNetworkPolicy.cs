using System.Globalization;
using System.Text.Json;

namespace Abox.Sbx;

// Strictly recognize observed supported policy metadata, never infer kit intent from reachability.
internal static class UserSecretsNetworkPolicy
{
    internal static IReadOnlyList<int> Ports(JsonElement root, string sandbox)
    {
        ValidateObjects(root);
        var ports = new SortedSet<int>();
        foreach (var rule in root.GetProperty("rules").EnumerateArray())
        {
            var name = Text(rule, "name");
            var scope = Text(rule, "scope");
            var appliesTo = Text(rule, "applies_to");
            var type = Text(rule, "resource_type");
            var decision = Text(rule, "decision");
            var origin = Text(rule, "origin");
            var layer = Text(rule, "layer");
            var status = Text(rule, "status");
            var editable = rule.GetProperty("editable").GetBoolean();
            var sandboxId = Text(rule, "sandbox_id");
            var provenance = rule.GetProperty("provenance");
            var createdVia = Text(provenance, "created_via");
            var source = Text(provenance, "source");
            var actions = Strings(rule, "actions");
            var resources = Strings(rule, "resources");
            if (
                name != "kit:" + sandbox
                || scope != "sandbox:" + sandbox
                || appliesTo != scope
                || sandboxId != sandbox
                || type != "network"
                || decision != "allow"
                || origin != "scoped"
                || layer != "local"
                || status != "active"
                || editable
                || createdVia != "provisioned"
                || source != "sandboxes"
                || !actions.Contains("net:connect:tcp")
            )
                continue;
            foreach (var resource in resources)
                if (
                    resource.StartsWith("localhost:", StringComparison.Ordinal)
                    && int.TryParse(
                        resource.AsSpan(10),
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out var port
                    )
                    && port is >= 1 and <= 65535
                )
                    ports.Add(port);
        }
        return ports.ToArray();
    }

    internal static bool IsAllowed(JsonElement root, string sandbox, string target)
    {
        ValidateObjects(root);
        if (
            Text(root, "action") != "net:connect:tcp"
            || Text(root, "context") != "sandbox:" + sandbox
            || Text(root, "resource_type") != "net:domain"
            || Text(root, "resource_value") != target
            || Text(root, "target") != target
            || Text(root, "type") != "network"
        )
            throw new InvalidOperationException();
        return root.GetProperty("allowed").GetBoolean();
    }

    private static string Text(JsonElement root, string property) =>
        root.GetProperty(property).GetString() is { Length: > 0 } text
            ? text
            : throw new InvalidOperationException();

    private static string[] Strings(JsonElement root, string property) =>
        root.GetProperty(property)
            .EnumerateArray()
            .Select(value => value.GetString() ?? throw new InvalidOperationException())
            .ToArray();

    private static void ValidateObjects(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    throw new InvalidOperationException();
                ValidateObjects(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray())
                ValidateObjects(item);
    }
}
