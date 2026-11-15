using System.Text.Json.Nodes;
using Abox.Configuration;
using Abox.Infrastructure;
using Abox.Sbx;
using NSubstitute;

namespace Abox.Tests;

public sealed class UserSecretsPolicyTests
{
    [Fact]
    public async Task SandboxRuntimePolicyAloneSuppliesRewriteIntentWithoutLocalKitFiles()
    {
        using var fixture = new PolicyFixture();
        fixture.UseAddedRule();

        (await fixture.ReadAsync()).Should().Equal(1521);
        fixture
            .Requests[0]
            .Arguments.Should()
            .Equal("policy", "ls", "synthetic-sandbox", "--type", "network", "--json");
        fixture.Requests[1].Arguments[^1].Should().Be("localhost:1521");
        fixture.Requests.Should().OnlyContain(request => request.Arguments[0] == "policy");
    }

    [Theory]
    [InlineData("expires_at", "2000-01-01T00:00:00Z")]
    [InlineData("expires_at", "2999-01-01T00:00:00Z")]
    [InlineData("constraints", "unknown")]
    public async Task UnknownManualRuleLifetimeOrConstraintsCannotSupplyRewriteIntent(
        string property,
        string value
    )
    {
        using var fixture = new PolicyFixture();
        fixture.UseAddedRule();
        fixture.Rule[property] = value;

        (await fixture.ReadAsync()).Should().BeEmpty();
        fixture.Requests.Should().ContainSingle();
    }

    [Theory]
    [InlineData("name", "kit:synthetic-sandbox")]
    [InlineData("id", "33333333-3333-4333-8333-333333333333")]
    [InlineData("policy_id", "unknown")]
    [InlineData("scope", "global")]
    [InlineData("scope", "sandbox:another-sandbox")]
    [InlineData("applies_to", "all")]
    [InlineData("sandbox_id", "another-sandbox")]
    [InlineData("resource_type", "filesystem")]
    [InlineData("decision", "deny")]
    [InlineData("origin", "local")]
    [InlineData("layer", "org")]
    [InlineData("status", "inactive")]
    [InlineData("status", "expired")]
    [InlineData("created_via", "approval")]
    [InlineData("created_via", "provisioned")]
    [InlineData("source", "sandboxes")]
    [InlineData("expires_at", "2000-01-01T00:00:00Z")]
    public async Task OnlySupportedExactSandboxManualProvenanceCanDeclarePorts(
        string property,
        string value
    )
    {
        using var fixture = new PolicyFixture();
        fixture.UseAddedRule();
        if (property is "created_via" or "source" or "expires_at")
            fixture.Rule["provenance"]![property] = value;
        else
            fixture.Rule[property] = value;

        (await fixture.ReadAsync()).Should().BeEmpty();
        fixture.Requests.Should().ContainSingle();
    }

    [Theory]
    [InlineData("**")]
    [InlineData("localhost")]
    [InlineData("localhost:*")]
    [InlineData("*.localhost:1521")]
    [InlineData("remote.test:1521")]
    [InlineData("127.0.0.1:1521")]
    [InlineData("host.docker.internal:1521")]
    [InlineData("localhost:0")]
    [InlineData("localhost:65536")]
    [InlineData("localhost:+1521")]
    [InlineData("localhost:1521-1522")]
    [InlineData("localhost:1521/path")]
    public async Task BroadOrNonliteralManualResourcesCannotDeclarePortsEvenAlongsideAnExactPort(
        string resource
    )
    {
        using var fixture = new PolicyFixture();
        fixture.UseAddedRule();
        fixture.Rule["resources"] = new JsonArray(resource, "localhost:1521");

        (await fixture.ReadAsync()).Should().BeEmpty();
        fixture.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task ManualDeclarationsMustBeEditablePersistentTcpOnlyRules()
    {
        using var fixture = new PolicyFixture();
        fixture.UseAddedRule();
        fixture.Rule["actions"] = new JsonArray("net:connect:udp");
        (await fixture.ReadAsync()).Should().BeEmpty();
        fixture.Rule["actions"] = new JsonArray("net:connect:tcp", "net:connect:udp");
        (await fixture.ReadAsync()).Should().BeEmpty();
        fixture.Rule["actions"] = new JsonArray("net:connect:tcp");
        fixture.Rule["editable"] = false;
        (await fixture.ReadAsync()).Should().BeEmpty();
    }

    [Theory]
    [InlineData("id")]
    [InlineData("policy_id")]
    [InlineData("sandbox_id")]
    [InlineData("provenance")]
    [InlineData("status")]
    public async Task MissingRequiredManualMetadataFailsSafely(string property)
    {
        using var fixture = new PolicyFixture();
        fixture.UseAddedRule();
        fixture.Rule.Remove(property);

        await fixture.Read.Should().ThrowAsync<AboxException>();
    }

    [Fact]
    public async Task KitAndManualPortsMergeButAmbientGlobalRulesNeverSupplyCandidates()
    {
        using var fixture = new PolicyFixture();
        var kit = fixture.Rule.DeepClone();
        fixture.UseAddedRule();
        fixture.Rule["resources"] = new JsonArray("localhost:8004", "localhost:1521");
        fixture.Rules.Add(kit);
        fixture.AddGlobalRules();

        (await fixture.ReadAsync()).Should().Equal(1521, 8004);
        fixture.Requests.Should().HaveCount(3);
        fixture.Rule["scope"] = "global";
        fixture.Rules.Remove(kit);
        fixture.Requests.Clear();
        (await fixture.ReadAsync()).Should().BeEmpty();
        fixture.Requests.Should().ContainSingle();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OracleCopyUsesExplicitRuntimeManualPolicyWithoutKitFiles(bool copied)
    {
        using var cli = new UserSecretsCliHarness();
        using var fixture = new PolicyFixture(cli.Name);
        fixture.UseAddedRule();
        fixture.AddGlobalRules();
        cli.Configure("""{"userSecrets":["sample"]}""");
        const string json = """
            {"Oracle":"Data Source=(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST=localhost)(PORT=1521))(CONNECT_DATA=(SERVICE_NAME=synthetic-service)));User Id=synthetic-user;Password=synthetic-password",
             "Unmapped":"//localhost:5000/synthetic-service","Remote":"//remote.test:1521/synthetic-service"}
            """;
        string source;
        if (copied)
            source = cli.Store("sample", json);
        else
        {
            cli.Root.File(
                "folder/Api/Api.csproj",
                "<Project><PropertyGroup><UserSecretsId>sample</UserSecretsId></PropertyGroup></Project>"
            );
            source = cli.Root.File("folder/Api/appsettings.json", json);
        }
        cli.Sbx.GetUserSecretsHostPortsAsync(cli.Name, Arg.Any<CancellationToken>())
            .Returns(call => fixture.ReadAsync(call.Arg<CancellationToken>()));

        (await cli.Run("--verbose")).Should().Be(0, cli.Console.AllText);

        await cli
            .Sbx.Received(1)
            .WriteUserSecretsAsync(
                cli.Name,
                "sample",
                Arg.Is<string>(written =>
                    written.Contains("(HOST=host.docker.internal)(PORT=1521)")
                    && written.Contains("Password=synthetic-password")
                    && (
                        copied
                            ? written.Contains("//localhost:5000/synthetic-service")
                                && written.Contains("//remote.test:1521/synthetic-service")
                            : !written.Contains("\"Unmapped\"") && !written.Contains("\"Remote\"")
                    )
                ),
                Arg.Any<CancellationToken>()
            );
        File.ReadAllText(source).Should().Be(json);
        cli.Console.AllText.Should()
            .NotContain("synthetic-password")
            .And.NotContain("synthetic-service");
        fixture.Requests.Should().HaveCount(2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ForceCannotBypassManualPolicyEffectiveDenyOrMalformedMetadata(bool malformed)
    {
        using var cli = new UserSecretsCliHarness();
        using var fixture = new PolicyFixture(cli.Name);
        fixture.UseAddedRule();
        if (malformed)
            fixture.Rule.Remove("sandbox_id");
        else
            fixture.Check["allowed"] = false;
        cli.Configure("""{"userSecrets":["sample"]}""");
        const string json = """{"Oracle":"//localhost:1521/synthetic-service"}""";
        cli.Store("sample", json);
        cli.Sbx.GetUserSecretsHostPortsAsync(cli.Name, Arg.Any<CancellationToken>())
            .Returns(call => fixture.ReadAsync(call.Arg<CancellationToken>()));

        (await cli.Run("--verbose", "--force")).Should().Be(malformed ? 1 : 0, cli.Console.AllText);

        if (malformed)
        {
            await cli.NoWrites();
            cli.Console.AllText.Should().Contain("sbx policy ls <sandbox> --type network --json");
        }
        else
            await cli
                .Sbx.Received(1)
                .WriteUserSecretsAsync(cli.Name, "sample", json, Arg.Any<CancellationToken>());
        cli.Console.AllText.Should()
            .NotContain("synthetic-service")
            .And.NotContain("Safety override");
    }

    [Theory]
    [InlineData("name", "manual:synthetic-sandbox")]
    [InlineData("scope", "global")]
    [InlineData("scope", "sandbox:another-sandbox")]
    [InlineData("applies_to", "sandbox:another-sandbox")]
    [InlineData("sandbox_id", "another-sandbox")]
    [InlineData("resource_type", "filesystem")]
    [InlineData("decision", "deny")]
    [InlineData("origin", "global")]
    [InlineData("layer", "global")]
    [InlineData("status", "inactive")]
    [InlineData("created_via", "manual")]
    [InlineData("source", "manual")]
    public async Task UnrelatedOrMismatchedKitRulesDoNotBecomeRewriteIntent(
        string property,
        string value
    )
    {
        using var fixture = new PolicyFixture();
        if (property is "created_via" or "source")
            fixture.Rule["provenance"]![property] = value;
        else
            fixture.Rule[property] = value;

        var ports = await fixture.ReadAsync();

        ports.Should().BeEmpty();
        fixture.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task OnlyExactTcpLocalhostPortResourcesAreCandidates()
    {
        using var fixture = new PolicyFixture();
        fixture.Rule["resources"] = new JsonArray(
            "*",
            "localhost:*",
            "*.localhost:8004",
            "host.docker.internal:8004",
            "127.0.0.1:8004",
            "localhost:0",
            "localhost:65536",
            "localhost:-1",
            "localhost:+8004",
            "localhost: 8004",
            "localhost:8004/path",
            "localhost:8004 ",
            "https://localhost:8004",
            "localhost:8004-8006",
            "localhost:8006",
            "localhost:8004",
            "localhost:8006",
            "localhost:1",
            "localhost:65535"
        );

        (await fixture.ReadAsync()).Should().Equal(1, 8004, 8006, 65535);
        fixture
            .Requests.Skip(1)
            .Select(request => request.Arguments[^1])
            .Should()
            .Equal("localhost:1", "localhost:8004", "localhost:8006", "localhost:65535");
        fixture
            .Requests.Should()
            .OnlyContain(request => request.OutputPolicy == ProcessOutputPolicy.CapturedSensitive);
    }

    [Fact]
    public async Task UdpAndEditableRulesAreNotKitTcpDeclarations()
    {
        using var fixture = new PolicyFixture();
        fixture.Rule["actions"] = new JsonArray("net:connect:udp");
        (await fixture.ReadAsync()).Should().BeEmpty();
        fixture.Rule["actions"] = new JsonArray("net:connect:tcp");
        fixture.Rule["editable"] = true;
        (await fixture.ReadAsync()).Should().BeEmpty();
    }

    [Theory]
    [InlineData("name")]
    [InlineData("scope")]
    [InlineData("applies_to")]
    [InlineData("sandbox_id")]
    [InlineData("resource_type")]
    [InlineData("decision")]
    [InlineData("origin")]
    [InlineData("layer")]
    [InlineData("status")]
    [InlineData("editable")]
    [InlineData("provenance")]
    [InlineData("actions")]
    [InlineData("resources")]
    public async Task MissingRequiredInventoryMetadataFailsRatherThanGuessing(string property)
    {
        using var fixture = new PolicyFixture();
        fixture.Rule.Remove(property);

        await fixture
            .Read.Should()
            .ThrowAsync<AboxException>()
            .WithMessage("*policy ls*policy check*No policy was changed*");
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("{\"rules\":null}")]
    [InlineData("{\"rules\":[],\"rules\":[]}")]
    [InlineData("{\"rules\":[null]}")]
    [InlineData("synthetic-password 123456789 true")]
    public async Task MalformedInventoryFailsWithValueFreeActionableError(string json)
    {
        using var fixture = new PolicyFixture { InventoryOverride = json };

        var exception = (await fixture.Read.Should().ThrowAsync<AboxException>()).Which;

        exception.ToString().Should().NotContain("synthetic-password").And.NotContain("123456789");
        exception.Message.Should().Contain("policy ls").And.Contain("policy check");
        exception.InnerException.Should().BeNull();
    }

    [Theory]
    [InlineData("action", "net:connect:udp")]
    [InlineData("context", "global")]
    [InlineData("resource_type", "net:ip")]
    [InlineData("resource_value", "localhost:8006")]
    [InlineData("target", "localhost:8006")]
    [InlineData("type", "filesystem")]
    [InlineData("allowed", "true")]
    public async Task MismatchedOrMalformedEffectiveAuthorizationFailsSafely(
        string field,
        string value
    )
    {
        using var fixture = new PolicyFixture();
        fixture.Check[field] = value;

        await fixture.Read.Should().ThrowAsync<AboxException>();
    }

    [Theory]
    [InlineData("action")]
    [InlineData("context")]
    [InlineData("resource_type")]
    [InlineData("resource_value")]
    [InlineData("target")]
    [InlineData("type")]
    [InlineData("allowed")]
    public async Task MissingEffectiveAuthorizationMetadataCannotAuthorizeRewrite(string field)
    {
        using var fixture = new PolicyFixture();
        fixture.Check.Remove(field);

        await fixture.Read.Should().ThrowAsync<AboxException>();
    }

    [Theory]
    [InlineData("inventory")]
    [InlineData("check")]
    [InlineData("throw")]
    public async Task FailedCommandsDoNotExposeRawOutputOrExceptionDetails(string fail)
    {
        using var fixture = new PolicyFixture { Failure = fail };

        var exception = (await fixture.Read.Should().ThrowAsync<AboxException>()).Which;

        exception.ToString().Should().NotContain("synthetic-password").And.NotContain("123456789");
        exception.InnerException.Should().BeNull();
    }

    [Fact]
    public async Task EffectiveDenyIsNotOverriddenByTheKitAllow()
    {
        using var fixture = new PolicyFixture();
        fixture.Check["allowed"] = false;

        (await fixture.ReadAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task CancellationIsNotReportedAsMalformedMetadata()
    {
        using var fixture = new PolicyFixture { Failure = "cancel" };

        await fixture.Read.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task PreCancelledDiscoveryDoesNotStartAChildProcess()
    {
        using var fixture = new PolicyFixture();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        var action = () => fixture.ReadAsync(cancelled.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        fixture.Requests.Should().BeEmpty();
    }

    private sealed class PolicyFixture : IDisposable
    {
        private readonly TempDirectory root = new();
        private readonly SbxClient client;
        private readonly JsonObject inventory;
        private readonly string sandbox;

        internal PolicyFixture(string sandbox = "synthetic-sandbox")
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
                        return new ProcessResult(
                            0,
                            InventoryOverride ?? inventory.ToJsonString(),
                            ""
                        );
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
}
