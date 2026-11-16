using Abox.Infrastructure;
using Abox.Launchers;
using Abox.Sbx;
using NSubstitute;

namespace Abox.Tests;

public sealed class HerdrAppLauncherTests
{
    [Theory]
    [InlineData("actual")]
    [InlineData("folder-ai-sbx-abc123")]
    public async Task HealthyAppUsesActualIdentityRuntimeWorkspaceAndSerializedArgv(string sandbox)
    {
        // Arrange
        var fixture = new HerdrProtocolFixture($"{{\"machines\":[{{\"name\":\"{sandbox}\"}}]}}");
        var context = fixture.Context with
        {
            SandboxName = sandbox,
            Argv = ["tool", "space and \"quote\"", "$(not-a-shell)", "Grüße"],
        };

        // Act
        var result = await new HerdrAppLauncher(fixture.Runner).RunAsync(
            context,
            CancellationToken.None
        );

        // Assert
        result.Should().Be(0);
        fixture
            .Calls.Should()
            .NotContain(r => r.Arguments.Count == 0 || r.Arguments.Contains("add"));
        var opened = fixture.Calls.Single(r => r.Arguments.Contains("space.new"));
        opened
            .Arguments.Take(8)
            .Should()
            .Equal(
                "do",
                "space.new",
                "--name",
                "folder",
                "--folder",
                "/runtime/workspace",
                "--machine",
                sandbox
            );
        System
            .Text.Json.JsonSerializer.Deserialize<string[]>(opened.Arguments[9])
            .Should()
            .Equal(context.Argv);
    }

    [Fact]
    public async Task FailedRegistrationDoesNotStartTheDesktop()
    {
        // Arrange
        var fixture = new HerdrProtocolFixture("{\"machines\":[]}")
        {
            AddResult = new(9, "", "registration rejected"),
        };

        // Act
        var action = fixture.LaunchAsync;

        // Assert
        (await action.Should().ThrowAsync<AboxException>())
            .Which.Message.Should()
            .Contain("machine add")
            .And.Contain("9");
        fixture.Calls.Should().NotContain(r => r.FileName == "herdr-app");
    }

    [Fact]
    public async Task FailedSpaceOpenIsNotReportedAsSuccess()
    {
        // Arrange
        var fixture = new HerdrProtocolFixture { SpaceResult = new(23, "", "") };

        // Act
        var action = fixture.LaunchAsync;

        // Assert
        (await action.Should().ThrowAsync<AboxException>())
            .Which.Message.Should()
            .Contain("space.new")
            .And.Contain("23");
    }

    [Fact]
    public async Task StaleSandboxParentDoesNotOpenDesktopOrReportSuccessfulLaunch()
    {
        // Arrange
        var runner = Substitute.For<IProcessRunner>();
        runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(0, "actual", ""));
        var sbx = Substitute.For<ISbxClient>();
        sbx.ExecAsync(Arg.Any<SbxExecRequest>(), Arg.Any<CancellationToken>()).Returns(42);
        var launcher = new HerdrAppLauncher(runner);
        var context = new LauncherContext(
            "location",
            "actual",
            "/workspace",
            [],
            sbx,
            new TestConsole()
        );

        // Act
        var action = () => launcher.RunAsync(context, CancellationToken.None);

        // Assert
        var failure = (await action.Should().ThrowAsync<AboxException>()).Which;
        failure.Message.Should().Contain("fresh").And.Contain("not opened");
        await runner
            .DidNotReceive()
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>());
    }
}
