using Abox.Infrastructure;

namespace Abox.Tests;

public sealed class HerdrReadinessTests
{
    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("123")]
    [InlineData("{}")]
    [InlineData("{\"errors\":null}")]
    [InlineData("{\"errors\":{}}")]
    [InlineData("{\"errors\":\"\"}")]
    [InlineData("{\"errors\":[null]}")]
    [InlineData("{\"errors\":[123]}")]
    [InlineData("{\"errors\":[],\"error\":\"bad command\"}")]
    public async Task InvalidStatusNeverStartsAnotherAppOrOpensASpace(string status)
    {
        // Arrange
        var fixture = new HerdrProtocolFixture();
        fixture.Statuses.Enqueue(new ProcessResult(0, status, ""));

        // Act
        var action = fixture.LaunchAsync;

        // Assert
        (await action.Should().ThrowAsync<AboxException>())
            .Which.Message.Should()
            .Contain("herdr-app status");
        fixture
            .Calls.Should()
            .NotContain(r => r.FileName == "herdr-app" && !r.Arguments.Contains("status"));
    }

    [Fact]
    public async Task ReportedAppErrorsAreNotAnInvitationToRestartIt()
    {
        // Arrange
        var fixture = new HerdrProtocolFixture();
        fixture.Statuses.Enqueue(
            new ProcessResult(
                0,
                "{\"errors\":[\"layout broken: synthetic-secret\\u001b[31m\\nforged warning\"]}",
                ""
            )
        );

        // Act
        var result = await fixture.LaunchAsync();

        // Assert
        result.Should().Be(0);
        fixture
            .Calls.Should()
            .NotContain(r => r.Arguments.Count == 0 || r.Arguments.Contains("add"));
        fixture.Calls[^1].Arguments.Should().Contain("space.new");
        var console = fixture.Console;
        console
            .Errors.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("configuration diagnostics")
            .And.Contain("herdr-app status");
        console
            .AllText.Should()
            .NotContain("synthetic-secret")
            .And.NotContain("\u001b")
            .And.NotContain("forged warning");
    }

    [Theory]
    [InlineData(1, "{\"error\":\"window did not answer\"}", "")]
    [InlineData(2, "", "unexpected control failure")]
    [InlineData(2, "null", "herdr-app: no Herdr window at /synthetic/socket: refused")]
    public async Task FailedStatusOtherThanConfirmedMissingWindowDoesNotLaunch(
        int exitCode,
        string stdout,
        string stderr
    )
    {
        // Arrange
        var fixture = new HerdrProtocolFixture();
        fixture.Statuses.Enqueue(new ProcessResult(exitCode, stdout, stderr));

        // Act
        var action = fixture.LaunchAsync;

        // Assert
        (await action.Should().ThrowAsync<AboxException>())
            .Which.Message.Should()
            .Contain("herdr-app status");
        fixture
            .Calls.Should()
            .NotContain(r => r.Arguments.Count == 0 || r.Arguments.Contains("space.new"));
    }
}
