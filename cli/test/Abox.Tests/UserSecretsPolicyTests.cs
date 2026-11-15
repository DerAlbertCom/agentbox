using System.Text.Json.Nodes;
using Abox.Configuration;
using Abox.Infrastructure;
using Abox.Sbx;
using NSubstitute;

namespace Abox.Tests;

public sealed class UserSecretsPolicyTests
{
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
    public async Task UnrelatedOrNonKitRulesDoNotBecomeRewriteIntent(string property, string value)
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

        internal PolicyFixture()
        {
            inventory = JsonNode
                .Parse(
                    """
                    {"rules":[{"name":"kit:synthetic-sandbox","scope":"sandbox:synthetic-sandbox",
                    "applies_to":"sandbox:synthetic-sandbox","resource_type":"network","decision":"allow",
                    "origin":"scoped","layer":"local","status":"active","editable":false,
                    "sandbox_id":"synthetic-sandbox","provenance":{"created_via":"provisioned","source":"sandboxes"},
                    "actions":["net:connect:tcp"],"resources":["localhost:8004"]}]}
                    """
                )!
                .AsObject();
            Check = JsonNode
                .Parse(
                    """
                    {"action":"net:connect:tcp","allowed":true,"context":"sandbox:synthetic-sandbox",
                    "resource_type":"net:domain","resource_value":"localhost:8004","target":"localhost:8004","type":"network"}
                    """
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

        internal JsonObject Rule => inventory["rules"]![0]!.AsObject();
        internal JsonObject Check { get; }
        internal string? InventoryOverride { get; init; }
        internal string? Failure { get; init; }
        internal List<ProcessRequest> Requests { get; } = [];
        internal Func<Task<IReadOnlyList<int>>> Read => () => ReadAsync();

        internal Task<IReadOnlyList<int>> ReadAsync(CancellationToken token = default) =>
            client.GetUserSecretsHostPortsAsync("synthetic-sandbox", token);

        public void Dispose() => root.Dispose();
    }
}
