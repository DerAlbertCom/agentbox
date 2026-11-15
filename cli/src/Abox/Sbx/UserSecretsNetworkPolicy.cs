using System.Globalization;
using System.Text.Json;

namespace Abox.Sbx;

// Recognize explicit runtime declarations, never infer rewrite intent from reachability.
internal static class UserSecretsNetworkPolicy
{
    internal static IReadOnlyList<int> Ports(JsonElement root, string sandbox)
    {
        ValidateObjects(root);
        var ports = new SortedSet<int>();
        foreach (var rule in root.GetProperty("rules").EnumerateArray())
        {
            var scope = Text(rule, "scope");
            if (scope != "sandbox:" + sandbox)
                continue;
            var name = Text(rule, "name");
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
            var actions = Strings(rule, "actions");
            var resources = Strings(rule, "resources");
            if (
                appliesTo != scope
                || sandboxId != sandbox
                || type != "network"
                || decision != "allow"
                || origin != "scoped"
                || layer != "local"
                || status != "active"
                || !actions.Contains("net:connect:tcp")
            )
                continue;
            var provisioned =
                name == "kit:" + sandbox
                && !editable
                && createdVia == "provisioned"
                && Text(provenance, "source") == "sandboxes";
            var added =
                editable
                && createdVia == "added"
                && name == Text(rule, "id")
                && Guid.TryParseExact(name, "D", out _)
                && Guid.TryParseExact(Text(rule, "policy_id"), "D", out _)
                && provenance.EnumerateObject().Count() == 1
                && KnownAddedRuleFields(rule)
                && actions.Length == 1
                && resources.Length > 0
                && resources.All(resource => LocalhostPort(resource) is not null);
            if (!provisioned && !added)
                continue;
            foreach (var resource in resources)
                if (LocalhostPort(resource) is { } port)
                    ports.Add(port);
        }
        return ports.ToArray();
    }

    // The observed persistent-rule schema has no expiry or conditional constraints.
    private static bool KnownAddedRuleFields(JsonElement rule) =>
        rule.EnumerateObject()
            .All(property =>
                property.Name
                    is "id"
                        or "name"
                        or "policy_id"
                        or "scope"
                        or "applies_to"
                        or "resource_type"
                        or "decision"
                        or "resources"
                        or "origin"
                        or "layer"
                        or "status"
                        or "editable"
                        or "sandbox_id"
                        or "provenance"
                        or "actions"
            );

    private static int? LocalhostPort(string resource) =>
        resource.StartsWith("localhost:", StringComparison.Ordinal)
        && int.TryParse(
            resource.AsSpan(10),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var port
        )
        && port is >= 1 and <= 65535
            ? port
            : null;

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
