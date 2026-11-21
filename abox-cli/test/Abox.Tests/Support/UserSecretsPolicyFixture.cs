using System.Text.Json.Nodes;
using Abox.Configuration;
using Abox.Infrastructure;
using Abox.Sbx;
using NSubstitute;

namespace Abox.Tests;

internal sealed class UserSecretsPolicyFixture : IDisposable
{
    private readonly TempDirectory root = new();
    private readonly SbxClient client;
    private readonly JsonObject inventory;
    private readonly string sandbox;

    internal UserSecretsPolicyFixture(string sandbox = "synthetic-sandbox")
    {
        this.sandbox = sandbox;
        inventory = JsonNode
            .Parse(
                """
                {"rules":[{"name":"kit:synthetic-sandbox","scope":"sandbox:synthetic-sandbox",
                "applies_to":"sandbox:synthetic-sandbox","resource_type":"network","decision":"allow",
                "origin":"scoped","layer":"local","status":"active","editable":false,
                "sandbox_id":"synthetic-sandbox","provenance":{"created_via":"provisioned","source":"sandboxes"},
                "actions":["net:connect:tcp"],"resources":["localhost:8004"]}]}
                """.Replace("synthetic-sandbox", sandbox, StringComparison.Ordinal)
            )!
            .AsObject();
        Check = JsonNode
            .Parse(
                """
                {"action":"net:connect:tcp","allowed":true,"context":"sandbox:synthetic-sandbox",
                "resource_type":"net:domain","resource_value":"localhost:8004","target":"localhost:8004","type":"network"}
                """.Replace("synthetic-sandbox", sandbox, StringComparison.Ordinal)
            )!
            .AsObject();
        var runner = Substitute.For<IProcessRunner>();
        runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var request = call.Arg<ProcessRequest>();
                Requests.Add(request);
                if (Failure == "throw")
                    throw new AboxException("synthetic-password 123456789 true");
                if (Failure == "cancel")
                    throw new OperationCanceledException();
                var list = request.Arguments[1] == "ls";
                if (Failure == (list ? "inventory" : "check"))
                    return new ProcessResult(9, "synthetic-password", "123456789 true");
                if (list)
                    return new ProcessResult(0, InventoryOverride ?? inventory.ToJsonString(), "");
                var check = Check.DeepClone().AsObject();
                if (Check["target"]?.GetValue<string>() == "localhost:8004")
                    check["target"] = request.Arguments[^1];
                if (Check["resource_value"]?.GetValue<string>() == "localhost:8004")
                    check["resource_value"] = request.Arguments[^1];
                return new ProcessResult(0, check.ToJsonString(), "");
            });
        client = new SbxClient(runner, AboxPaths.For(root.Path, root.Path));
    }

    internal JsonArray Rules => inventory["rules"]!.AsArray();
    internal JsonObject Rule => Rules[0]!.AsObject();

    internal void AddGlobalRules()
    {
        Rules.Add(
            JsonNode.Parse(
                """
                {"id":"default-allow-all","name":"default-allow-all","policy_id":"local-policy",
                "scope":"global","applies_to":"all","resource_type":"network","decision":"allow",
                "resources":["**","localhost:4999"],"origin":"local","layer":"local","status":"active",
                "editable":true,"provenance":{"created_via":"added"},"actions":["net:connect:tcp"]}
                """
            )
        );
        Rules.Add(
            JsonNode.Parse(
                """
                {"id":"default-deny-all","name":"default-deny-all","scope":"global","applies_to":"all",
                "resource_type":"network","decision":"deny","resources":["**"],"origin":"local",
                "layer":"local","status":"active","editable":false,"actions":["net:connect:tcp","net:connect:udp"]}
                """
            )
        );
    }

    internal void UseAddedRule()
    {
        Rule["id"] = "11111111-1111-4111-8111-111111111111";
        Rule["name"] = "11111111-1111-4111-8111-111111111111";
        Rule["policy_id"] = "22222222-2222-4222-8222-222222222222";
        Rule["editable"] = true;
        Rule["provenance"] = new JsonObject { ["created_via"] = "added" };
        Rule["resources"] = new JsonArray("localhost:1521");
    }

    internal JsonObject Check { get; }
    internal string? InventoryOverride { get; init; }
    internal string? Failure { get; init; }
    internal List<ProcessRequest> Requests { get; } = [];
    internal Func<Task<IReadOnlyList<int>>> Read => () => ReadAsync();

    internal Task<IReadOnlyList<int>> ReadAsync(CancellationToken token = default) =>
        client.GetUserSecretsHostPortsAsync(sandbox, token);

    public void Dispose() => root.Dispose();
}
