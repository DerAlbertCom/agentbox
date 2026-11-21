using Abox.Infrastructure;

namespace Abox.Tests;

public sealed class UserSecretsPolicyConcurrencyTests
{
    [Fact]
    public async Task AllCandidatesOverlapWithinFourAndResultsIgnoreCompletionOrder()
    {
        // Arrange
        using var fixture = new GatedUserSecretsPolicyRunner(
            "synthetic-sandbox",
            "localhost:8009",
            "localhost:8003",
            "localhost:08001",
            "localhost:8002",
            "localhost:8008",
            "localhost:8004",
            "localhost:8001",
            "localhost:8007",
            "localhost:8006",
            "localhost:8005",
            "localhost:*",
            "remote.test:9000"
        );
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        // Act
        var read = fixture.ReadAsync(cancellation.Token);
        var first = new List<int>();
        for (var index = 0; index < 4; index++)
            first.Add(await fixture.NextStartedAsync());

        // Assert
        first.Should().BeEquivalentTo(new[] { 8001, 8002, 8003, 8004 });
        fixture.Active.Should().Be(4);
        read.IsCompleted.Should().BeFalse();

        // Act
        fixture.Complete(8004);
        (await fixture.NextStartedAsync()).Should().Be(8005);
        fixture.Complete(8002, allowed: false);
        (await fixture.NextStartedAsync()).Should().Be(8006);
        fixture.Complete(8006);
        (await fixture.NextStartedAsync()).Should().Be(8007);
        fixture.Complete(8005, allowed: false);
        (await fixture.NextStartedAsync()).Should().Be(8008);
        fixture.Complete(8007);
        (await fixture.NextStartedAsync()).Should().Be(8009);
        fixture.Complete(8009);
        fixture.Complete(8008, allowed: false);
        fixture.Complete(8003);

        // Assert
        read.IsCompleted.Should().BeFalse("the first candidate is still pending");

        // Act
        fixture.Complete(8001);
        var ports = await read;

        // Assert
        ports.Should().Equal(8001, 8003, 8004, 8006, 8007, 8009);
        fixture.MaximumActive.Should().Be(4);
        fixture.Active.Should().Be(0);
        fixture.Requests.Count(request => request.Arguments[1] == "ls").Should().Be(1);
        fixture.Requests.Count.Should().Be(10);
        fixture
            .Requests.Should()
            .OnlyContain(request => request.OutputPolicy == ProcessOutputPolicy.CapturedSensitive);
    }

    [Theory]
    [InlineData("malformed")]
    [InlineData("target")]
    [InlineData("protocol")]
    [InlineData("sandbox")]
    [InlineData("duplicate")]
    [InlineData("missing")]
    [InlineData("exit")]
    [InlineData("throw")]
    public async Task FailedConcurrentCheckCancelsAndAwaitsPeersWithoutReturningPartialAuthorization(
        string failure
    )
    {
        // Arrange
        using var fixture = new GatedUserSecretsPolicyRunner(
            "synthetic-sandbox",
            "localhost:8001",
            "localhost:8002",
            "localhost:8003",
            "localhost:8004"
        )
        {
            HoldCancellationCleanup = true,
        };
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var read = fixture.ReadAsync(cancellation.Token);
        for (var index = 0; index < 4; index++)
            await fixture.NextStartedAsync();
        fixture.Complete(8001);
        (await fixture.NextFinishedAsync()).Should().Be(8001);
        var valid = GatedUserSecretsPolicyRunner.Authorization(fixture.Sandbox, 8002);

        // Act
        if (failure == "throw")
            fixture.Fail(8002);
        else
            fixture.CompleteRaw(
                8002,
                new ProcessResult(
                    failure == "exit" ? 9 : 0,
                    failure switch
                    {
                        "malformed" => "synthetic-password 123456789 true",
                        "target" => valid.Replace("localhost:8002", "localhost:9000"),
                        "protocol" => valid.Replace("net:connect:tcp", "net:connect:udp"),
                        "sandbox" => valid.Replace("sandbox:synthetic-sandbox", "sandbox:other"),
                        "duplicate" => valid.Replace("{", "{\"allowed\":true,"),
                        "missing" => "{}",
                        _ => valid,
                    },
                    "synthetic-password 123456789 true"
                )
            );
        var cancelled = new[]
        {
            await fixture.NextCancelledAsync(),
            await fixture.NextCancelledAsync(),
        };

        // Assert
        cancelled.Should().BeEquivalentTo(new[] { 8003, 8004 });
        read.IsCompleted.Should().BeFalse("cancelled process cleanup is still gated");
        fixture.Active.Should().Be(2);

        // Act
        fixture.ReleaseCleanup();
        var action = async () => await read.WaitAsync(TimeSpan.FromSeconds(15));

        // Assert
        var exception = (await action.Should().ThrowAsync<AboxException>()).Which;
        exception.ToString().Should().NotContain("synthetic-password").And.NotContain("123456789");
        exception.InnerException.Should().BeNull();
        fixture.Active.Should().Be(0);
        fixture.Requests.Should().HaveCount(5);
    }

    [Fact]
    public async Task CancellationDuringChecksAwaitsEveryProcessCleanupAndDoesNotStartQueuedCandidates()
    {
        // Arrange
        using var fixture = new GatedUserSecretsPolicyRunner(
            "synthetic-sandbox",
            "localhost:8001",
            "localhost:8002",
            "localhost:8003",
            "localhost:8004",
            "localhost:8005"
        )
        {
            HoldCancellationCleanup = true,
        };
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var read = fixture.ReadAsync(cancellation.Token);
        for (var index = 0; index < 4; index++)
            await fixture.NextStartedAsync();

        // Act
        await cancellation.CancelAsync();
        for (var index = 0; index < 4; index++)
            await fixture.NextCancelledAsync();

        // Assert
        read.IsCompleted.Should().BeFalse("all child cleanup remains gated");
        fixture.Active.Should().Be(4);

        // Act
        fixture.ReleaseCleanup();
        var action = async () => await read.WaitAsync(TimeSpan.FromSeconds(15));

        // Assert
        await action.Should().ThrowAsync<OperationCanceledException>();
        fixture.Active.Should().Be(0);
        fixture.Requests.Should().HaveCount(5);
    }

    [Fact]
    public async Task CancellationBeforeInventoryStartsNoProcesses()
    {
        // Arrange
        using var fixture = new GatedUserSecretsPolicyRunner("synthetic-sandbox", "localhost:8001");
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // Act
        var action = () => fixture.ReadAsync(cancellation.Token);

        // Assert
        await action.Should().ThrowAsync<OperationCanceledException>();
        fixture.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task EmptyInventoryNeedsNoAuthorizationProcesses()
    {
        // Arrange
        using var fixture = new GatedUserSecretsPolicyRunner("synthetic-sandbox");

        // Act
        var ports = await fixture.ReadAsync();

        // Assert
        ports.Should().BeEmpty();
        fixture.Requests.Should().ContainSingle();
        fixture.Active.Should().Be(0);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SingleCandidateWaitsForItsEffectiveAllowOrDeny(bool allowed)
    {
        // Arrange
        using var fixture = new GatedUserSecretsPolicyRunner("synthetic-sandbox", "localhost:8001");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        // Act
        var read = fixture.ReadAsync(cancellation.Token);
        (await fixture.NextStartedAsync()).Should().Be(8001);

        // Assert
        read.IsCompleted.Should().BeFalse();

        // Act
        fixture.Complete(8001, allowed);
        var ports = await read;

        // Assert
        ports.Should().Equal(allowed ? new[] { 8001 } : []);
        fixture.Requests.Should().HaveCount(2);
        fixture.MaximumActive.Should().Be(1);
        fixture.Active.Should().Be(0);
    }
}
