using System.Text.Json.Nodes;
using Abox.Configuration;
using Abox.Infrastructure;
using Abox.Sbx;
using NSubstitute;

namespace Abox.Tests;

public sealed class SbxUserSecretsPolicyTests
{
    [Fact]
    public async Task SandboxRuntimePolicyAloneSuppliesRewriteIntentWithoutLocalKitFiles()
    {
        // Arrange
        using var fixture = new UserSecretsPolicyFixture();
        fixture.UseAddedRule();

        // Act
        var ports = await fixture.ReadAsync();

        // Assert
        ports.Should().Equal(1521);
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
        // Arrange
        using var fixture = new UserSecretsPolicyFixture();
        fixture.UseAddedRule();
        fixture.Rule[property] = value;

        // Act
        var ports = await fixture.ReadAsync();

        // Assert
        ports.Should().BeEmpty();
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
        // Arrange
        using var fixture = new UserSecretsPolicyFixture();
        fixture.UseAddedRule();
        if (property is "created_via" or "source" or "expires_at")
            fixture.Rule["provenance"]![property] = value;
        else
            fixture.Rule[property] = value;

        // Act
        var ports = await fixture.ReadAsync();

        // Assert
        ports.Should().BeEmpty();
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
        // Arrange
        using var fixture = new UserSecretsPolicyFixture();
        fixture.UseAddedRule();
        fixture.Rule["resources"] = new JsonArray(resource, "localhost:1521");

        // Act
        var ports = await fixture.ReadAsync();

        // Assert
        ports.Should().BeEmpty();
        fixture.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task ManualDeclarationsMustBeEditablePersistentTcpOnlyRules()
    {
        // Arrange
        using var fixture = new UserSecretsPolicyFixture();
        fixture.UseAddedRule();
        fixture.Rule["actions"] = new JsonArray("net:connect:udp");

        // Act
        var udpPorts = await fixture.ReadAsync();

        // Assert
        udpPorts.Should().BeEmpty();

        // Arrange
        fixture.Rule["actions"] = new JsonArray("net:connect:tcp", "net:connect:udp");

        // Act
        var mixedPorts = await fixture.ReadAsync();

        // Assert
        mixedPorts.Should().BeEmpty();

        // Arrange
        fixture.Rule["actions"] = new JsonArray("net:connect:tcp");
        fixture.Rule["editable"] = false;

        // Act
        var uneditablePorts = await fixture.ReadAsync();

        // Assert
        uneditablePorts.Should().BeEmpty();
    }

    [Theory]
    [InlineData("id")]
    [InlineData("policy_id")]
    [InlineData("sandbox_id")]
    [InlineData("provenance")]
    [InlineData("status")]
    public async Task MissingRequiredManualMetadataFailsSafely(string property)
    {
        // Arrange
        using var fixture = new UserSecretsPolicyFixture();
        fixture.UseAddedRule();
        fixture.Rule.Remove(property);

        // Act
        var action = fixture.Read;

        // Assert
        await action.Should().ThrowAsync<AboxException>();
    }

    [Fact]
    public async Task KitAndManualPortsMergeButAmbientGlobalRulesNeverSupplyCandidates()
    {
        // Arrange
        using var fixture = new UserSecretsPolicyFixture();
        var kit = fixture.Rule.DeepClone();
        fixture.UseAddedRule();
        fixture.Rule["resources"] = new JsonArray("localhost:8004", "localhost:1521");
        fixture.Rules.Add(kit);
        fixture.AddGlobalRules();

        // Act
        var ports = await fixture.ReadAsync();

        // Assert
        ports.Should().Equal(1521, 8004);
        fixture.Requests.Should().HaveCount(3);

        // Arrange
        fixture.Rule["scope"] = "global";
        fixture.Rules.Remove(kit);
        fixture.Requests.Clear();
        // Act
        var globalPorts = await fixture.ReadAsync();

        // Assert
        globalPorts.Should().BeEmpty();
        fixture.Requests.Should().ContainSingle();
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
        // Arrange
        using var fixture = new UserSecretsPolicyFixture();
        if (property is "created_via" or "source")
            fixture.Rule["provenance"]![property] = value;
        else
            fixture.Rule[property] = value;

        // Act
        var ports = await fixture.ReadAsync();

        // Assert
        ports.Should().BeEmpty();
        fixture.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task OnlyExactTcpLocalhostPortResourcesAreCandidates()
    {
        // Arrange
        using var fixture = new UserSecretsPolicyFixture();
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

        // Act
        var ports = await fixture.ReadAsync();

        // Assert
        ports.Should().Equal(1, 8004, 8006, 65535);
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
        // Arrange
        using var fixture = new UserSecretsPolicyFixture();
        fixture.Rule["actions"] = new JsonArray("net:connect:udp");

        // Act
        var udpPorts = await fixture.ReadAsync();

        // Assert
        udpPorts.Should().BeEmpty();

        // Arrange
        fixture.Rule["actions"] = new JsonArray("net:connect:tcp");
        fixture.Rule["editable"] = true;
        // Act
        var editablePorts = await fixture.ReadAsync();

        // Assert
        editablePorts.Should().BeEmpty();
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
        // Arrange
        using var fixture = new UserSecretsPolicyFixture();
        fixture.Rule.Remove(property);

        // Act
        var action = fixture.Read;

        // Assert
        await action
            .Should()
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
        // Arrange
        using var fixture = new UserSecretsPolicyFixture { InventoryOverride = json };

        // Act
        var action = fixture.Read;

        // Assert
        var exception = (await action.Should().ThrowAsync<AboxException>()).Which;

        exception.ToString().Should().NotContain("synthetic-password").And.NotContain("123456789");
        exception.Message.Should().Contain("policy ls").And.Contain("policy check");
        exception.InnerException.Should().BeNull();
    }

    [Theory]
    [InlineData("\"editable\":false", "\"editable\":true,\"editable\":false")]
    [InlineData(
        "\"created_via\":\"provisioned\"",
        "\"created_via\":\"added\",\"created_via\":\"provisioned\""
    )]
    [InlineData("{\"rules\":", "{\"extra\":[{\"constraint\":1,\"constraint\":2}],\"rules\":")]
    public async Task DuplicateObjectsCannotSupplyRewriteIntentEvenOutsideRecognizedFields(
        string original,
        string replacement
    )
    {
        // Arrange
        var inventory = GatedUserSecretsPolicyRunner.Inventory(
            "synthetic-sandbox",
            "localhost:8004"
        );
        using var fixture = new UserSecretsPolicyFixture
        {
            InventoryOverride = inventory.Replace(original, replacement, StringComparison.Ordinal),
        };

        // Act
        var action = fixture.Read;

        // Assert
        await action.Should().ThrowAsync<AboxException>().WithMessage("*No policy was changed*");
        fixture.Requests.Should().ContainSingle();
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
        // Arrange
        using var fixture = new UserSecretsPolicyFixture();
        fixture.Check[field] = value;

        // Act
        var action = fixture.Read;

        // Assert
        await action.Should().ThrowAsync<AboxException>();
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
        // Arrange
        using var fixture = new UserSecretsPolicyFixture();
        fixture.Check.Remove(field);

        // Act
        var action = fixture.Read;

        // Assert
        await action.Should().ThrowAsync<AboxException>();
    }

    [Theory]
    [InlineData("inventory")]
    [InlineData("check")]
    [InlineData("throw")]
    public async Task FailedCommandsDoNotExposeRawOutputOrExceptionDetails(string fail)
    {
        // Arrange
        using var fixture = new UserSecretsPolicyFixture { Failure = fail };

        // Act
        var action = fixture.Read;

        // Assert
        var exception = (await action.Should().ThrowAsync<AboxException>()).Which;

        exception.ToString().Should().NotContain("synthetic-password").And.NotContain("123456789");
        exception.InnerException.Should().BeNull();
    }

    [Fact]
    public async Task EffectiveDenyIsNotOverriddenByTheKitAllow()
    {
        // Arrange
        using var fixture = new UserSecretsPolicyFixture();
        fixture.Check["allowed"] = false;

        // Act
        var ports = await fixture.ReadAsync();

        // Assert
        ports.Should().BeEmpty();
    }

    [Fact]
    public async Task CancellationIsNotReportedAsMalformedMetadata()
    {
        // Arrange
        using var fixture = new UserSecretsPolicyFixture { Failure = "cancel" };

        // Act
        var action = fixture.Read;

        // Assert
        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task PreCancelledDiscoveryDoesNotStartAChildProcess()
    {
        // Arrange
        using var fixture = new UserSecretsPolicyFixture();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        // Act
        var action = () => fixture.ReadAsync(cancelled.Token);

        // Assert
        await action.Should().ThrowAsync<OperationCanceledException>();
        fixture.Requests.Should().BeEmpty();
    }
}
