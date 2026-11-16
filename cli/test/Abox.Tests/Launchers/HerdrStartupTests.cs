using Abox.Infrastructure;
using Abox.Launchers;
using NSubstitute;

namespace Abox.Tests;

public sealed class HerdrStartupTests
{
    private static ProcessResult MissingWindow =>
        new(2, "", "herdr-app: no Herdr window at /synthetic/herdr-app.sock: connection refused");

    [Fact]
    public async Task ReadinessDeadlineBoundsAStatusProcessThatNeverReplies()
    {
        // Arrange
        var fixture = new HerdrProtocolFixture();
        fixture
            .Runner.RunAsync(
                Arg.Is<ProcessRequest>(r => r.Arguments.SequenceEqual(new[] { "status" })),
                Arg.Any<CancellationToken>()
            )
            .Returns(async call =>
            {
                await Task.Delay(Timeout.Infinite, call.Arg<CancellationToken>());
                return MissingWindow;
            });

        // Act
        var action = () => fixture.LaunchAsync().WaitAsync(TimeSpan.FromSeconds(55));

        // Assert
        (await action.Should().ThrowAsync<AboxException>())
            .Which.Message.Should()
            .Contain("timed out")
            .And.Contain("45 seconds");
        fixture
            .Calls.Should()
            .NotContain(r => r.Arguments.Count == 0 || r.Arguments.Contains("space.new"));
    }

    [Fact]
    public async Task FailedAppStartDoesNotOpenASpace()
    {
        // Arrange
        var fixture = new HerdrProtocolFixture { StartupResult = new(17, "", "") };
        fixture.Statuses.Enqueue(MissingWindow);

        // Act
        var action = fixture.LaunchAsync;

        // Assert
        (await action.Should().ThrowAsync<AboxException>())
            .Which.Message.Should()
            .Contain("17");
        fixture.Calls.Should().NotContain(r => r.Arguments.Contains("space.new"));
    }

    [Fact]
    public async Task FreshDesktopMustConfirmHealthBeforeSpaceOpen()
    {
        // Arrange
        var fixture = new HerdrProtocolFixture();
        fixture.Statuses.Enqueue(MissingWindow);
        fixture.Statuses.Enqueue(MissingWindow);
        fixture.Statuses.Enqueue(new(0, "{\"errors\":[]}", ""));

        // Act
        await fixture.LaunchAsync();

        // Assert
        fixture
            .Calls.Where(r => r.FileName == "herdr-app")
            .Select(r => string.Join(" ", r.Arguments))
            .Should()
            .Equal(
                "status",
                "",
                "status",
                "status",
                "do space.new --name folder --folder /runtime/workspace --machine actual"
            );
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{\"errors\":[\"bad layout\"]}")]
    public async Task InvalidReadinessAfterStartingDoesNotOpenASpace(string response)
    {
        // Arrange
        var fixture = new HerdrProtocolFixture();
        fixture.Statuses.Enqueue(MissingWindow);
        fixture.Statuses.Enqueue(new(0, response, ""));

        // Act
        var action = fixture.LaunchAsync;

        // Assert
        await action.Should().ThrowAsync<AboxException>();
        fixture.Calls.Should().NotContain(r => r.Arguments.Contains("space.new"));
    }

    [Fact]
    public async Task WaitingForReadinessHonorsCancellationWithoutOpeningASpace()
    {
        // Arrange
        var fixture = new HerdrProtocolFixture();
        for (var index = 0; index < 10; index++)
            fixture.Statuses.Enqueue(MissingWindow);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        // Act
        var action = () =>
            new HerdrAppLauncher(fixture.Runner).RunAsync(fixture.Context, cancellation.Token);

        // Assert
        await action.Should().ThrowAsync<OperationCanceledException>();
        fixture.Calls.Should().NotContain(r => r.Arguments.Contains("space.new"));
    }
}
