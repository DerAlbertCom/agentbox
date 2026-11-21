using System.Text.Json;
using Abox.Naming;

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
            var declaration = new SandboxNetworkRule(rule);
            if (!declaration.IsActiveTcpAllowFor(sandbox))
                continue;
            if (!declaration.IsKitProvisioned(sandbox) && !declaration.IsPersistentLocal())
                continue;
            foreach (var resource in declaration.Resources)
                if (LocalhostPort(resource) is { } port)
                    ports.Add(port);
        }
        return ports.ToArray();
    }

    // Both supported schemas require complete metadata before classification. In particular,
    // an inactive or unrelated same-scope rule cannot hide malformed required fields.
    private sealed class SandboxNetworkRule(JsonElement rule)
    {
        private readonly string name = Text(rule, "name");
        private readonly string appliesTo = Text(rule, "applies_to");
        private readonly string type = Text(rule, "resource_type");
        private readonly string decision = Text(rule, "decision");
        private readonly string origin = Text(rule, "origin");
        private readonly string layer = Text(rule, "layer");
        private readonly string status = Text(rule, "status");
        private readonly bool editable = rule.GetProperty("editable").GetBoolean();
        private readonly string sandboxId = Text(rule, "sandbox_id");
        private readonly JsonElement provenance = rule.GetProperty("provenance");
        private readonly string createdVia = Text(rule.GetProperty("provenance"), "created_via");
        private readonly string[] actions = Strings(rule, "actions");
        internal string[] Resources { get; } = Strings(rule, "resources");

        internal bool IsActiveTcpAllowFor(string sandbox)
        {
            var exactSandbox = appliesTo == "sandbox:" + sandbox && sandboxId == sandbox;
            var localAllow = type == "network" && decision == "allow" && origin == "scoped";
            return exactSandbox && localAllow && IsActiveLocalTcp();
        }

        private bool IsActiveLocalTcp() =>
            layer == "local" && status == "active" && actions.Contains("net:connect:tcp");

        internal bool IsKitProvisioned(string sandbox) =>
            name == "kit:" + sandbox
            && !editable
            && createdVia == "provisioned"
            && Text(provenance, "source") == "sandboxes";

        internal bool IsPersistentLocal()
        {
            if (!editable || createdVia != "added")
                return false;
            if (!HasPersistentIdentity() || !HasOnlyPersistentFields())
                return false;
            // Unlike kit declarations, every resource/action in a local rule must be narrow.
            return actions.Length == 1
                && Resources.Length > 0
                && Resources.All(resource => LocalhostPort(resource) is not null);
        }

        private bool HasPersistentIdentity() =>
            name == Text(rule, "id")
            && Guid.TryParseExact(name, "D", out _)
            && Guid.TryParseExact(Text(rule, "policy_id"), "D", out _);

        private bool HasOnlyPersistentFields() =>
            provenance.EnumerateObject().Count() == 1 && KnownPersistentLocalFields(rule);
    }

    // The observed persistent-local schema has no expiry or conditional constraints.
    private static bool KnownPersistentLocalFields(JsonElement rule) =>
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
        && TcpPort.TryParse(resource.AsSpan(10), out var port)
            ? port.Value
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
