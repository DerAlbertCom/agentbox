using System.CommandLine;
using Abox.Cli;
using Abox.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.Extensions;

namespace Abox.Tests;

public sealed class UserSecretsPolicyCommandTests
{
    [Fact]
    public async Task CliInvocationCancellationReachesAllPolicyChecksAndPreventsEveryWrite()
    {
        // Arrange
        using var cli = new UserSecretsCliHarness();
        using var policy = new GatedUserSecretsPolicyRunner(
            cli.Name,
            "localhost:8001",
            "localhost:8002",
            "localhost:8003",
            "localhost:8004"
        );
        cli.Configure("""{"userSecrets":["first","second"]}""");
        cli.Store("first", """{"Url":"http://localhost:8001/path"}""");
        cli.Store("second", """{"Password":"synthetic-password"}""");
        cli.Sbx.GetUserSecretsHostPortsAsync(cli.Name, Arg.Any<CancellationToken>())
            .Returns(call => policy.ReadAsync(call.Arg<CancellationToken>()));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var parsed = cli
            .Provider.GetRequiredService<AboxRootCommand>()
            .Parse(["user-secrets", "--location", cli.Location]);

        // Act
        var run = parsed.InvokeAsync(cancellationToken: cancellation.Token);
        for (var index = 0; index < 4; index++)
            await policy.NextStartedAsync();
        await cancellation.CancelAsync();
        var exitCode = await run.WaitAsync(TimeSpan.FromSeconds(15));

        // Assert
        exitCode.Should().Be(130, cli.Console.AllText);
        await cli.NoWrites();
        policy.Active.Should().Be(0);
        policy.Requests.Should().HaveCount(5);
        cli.Console.AllText.Should().Contain("cancelled").And.NotContain("synthetic-password");
    }

    [Theory]
    [InlineData(false, "malformed")]
    [InlineData(true, "malformed")]
    [InlineData(false, "exit")]
    [InlineData(true, "exit")]
    [InlineData(false, "throw")]
    [InlineData(true, "throw")]
    [InlineData(false, "cancel")]
    [InlineData(true, "cancel")]
    public async Task PolicyFailureOrCancellationBlocksEveryOtherwiseValidStore(
        bool verbose,
        string outcome
    )
    {
        // Arrange
        using var cli = new UserSecretsCliHarness();
        using var policy = new GatedUserSecretsPolicyRunner(
            cli.Name,
            "localhost:8001",
            "localhost:8002",
            "localhost:8003",
            "localhost:8004"
        )
        {
            HoldCancellationCleanup = true,
        };
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        cli.Configure("""{"userSecrets":["first","second"]}""");
        const string firstJson = """{"Url":"http://localhost:8001/synthetic-password"}""";
        const string secondJson = """{"Password":"synthetic-password 123456789 true"}""";
        var first = cli.Store("first", firstJson);
        var second = cli.Store("second", secondJson);
        cli.Sbx.GetUserSecretsHostPortsAsync(cli.Name, Arg.Any<CancellationToken>())
            .Returns(_ => policy.ReadAsync(cancellation.Token));

        // Act
        var run = verbose ? cli.Run("--verbose", "--force") : cli.Run("--force");
        for (var index = 0; index < 4; index++)
            await policy.NextStartedAsync();
        policy.Complete(8001);
        (await policy.NextFinishedAsync()).Should().Be(8001);

        // Assert
        await cli.NoWrites();
        run.IsCompleted.Should().BeFalse();

        // Act
        if (outcome == "cancel")
            await cancellation.CancelAsync();
        else if (outcome == "throw")
            policy.Fail(8002);
        else
            policy.CompleteRaw(
                8002,
                new ProcessResult(
                    outcome == "exit" ? 9 : 0,
                    "synthetic-password 123456789 true",
                    "synthetic-password 123456789 true"
                )
            );
        for (var index = 0; index < (outcome == "cancel" ? 3 : 2); index++)
            await policy.NextCancelledAsync();

        // Assert
        run.IsCompleted.Should().BeFalse("the CLI must await in-flight policy cleanup");
        await cli.NoWrites();

        // Act
        policy.ReleaseCleanup();
        var exitCode = await run.WaitAsync(TimeSpan.FromSeconds(15));

        // Assert
        exitCode.Should().Be(outcome == "cancel" ? 130 : 1, cli.Console.AllText);
        await cli.NoWrites();
        policy.Active.Should().Be(0);
        File.ReadAllText(first).Should().Be(firstJson);
        File.ReadAllText(second).Should().Be(secondJson);
        cli.Console.AllText.Should().NotContain("synthetic-password").And.NotContain("123456789");
        cli.Console.AllText.Should().Contain(outcome == "cancel" ? "cancelled" : "policy check");
    }

    [Fact]
    public async Task MultipleStoresWaitForUnusedCandidatesAndEveryInvocationReauthorizes()
    {
        // Arrange
        using var cli = new UserSecretsCliHarness();
        cli.Configure("""{"userSecrets":["first","second"]}""");
        cli.Store("first", """{"Url":"http://localhost:8001/path"}""");
        cli.Store("second", """{"Password":"synthetic-password"}""");

        // Act
        for (var invocation = 0; invocation < 2; invocation++)
        {
            using var policy = new GatedUserSecretsPolicyRunner(
                cli.Name,
                "localhost:8001",
                "localhost:8002",
                "localhost:8003",
                "localhost:8004",
                "localhost:8005"
            );
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            cli.Sbx.Configure()
                .GetUserSecretsHostPortsAsync(cli.Name, Arg.Any<CancellationToken>())
                .Returns(_ => policy.ReadAsync(cancellation.Token));
            var run = cli.Run("--verbose");
            for (var index = 0; index < 4; index++)
                await policy.NextStartedAsync();
            policy.Complete(8001, allowed: invocation == 0);
            (await policy.NextStartedAsync()).Should().Be(8005);
            policy.Complete(8002);
            policy.Complete(8003);
            policy.Complete(8004);

            // Assert
            run.IsCompleted.Should()
                .BeFalse("even the unused last port must finish authorization");
            await cli
                .Sbx.Received(invocation * 2)
                .WriteUserSecretsAsync(
                    Arg.Any<string>(),
                    Arg.Any<string>(),
                    Arg.Any<string>(),
                    Arg.Any<CancellationToken>()
                );

            // Act
            policy.Complete(8005);
            var exitCode = await run.WaitAsync(TimeSpan.FromSeconds(15));

            // Assert
            exitCode.Should().Be(0, cli.Console.AllText);
            policy.Requests.Should().HaveCount(6);
        }

        // Assert
        await cli
            .Sbx.Received(1)
            .WriteUserSecretsAsync(
                cli.Name,
                "first",
                """{"Url":"http://host.docker.internal:8001/path"}""",
                Arg.Any<CancellationToken>()
            );
        await cli
            .Sbx.Received(1)
            .WriteUserSecretsAsync(
                cli.Name,
                "first",
                """{"Url":"http://localhost:8001/path"}""",
                Arg.Any<CancellationToken>()
            );
        await cli
            .Sbx.Received(2)
            .WriteUserSecretsAsync(
                cli.Name,
                "second",
                """{"Password":"synthetic-password"}""",
                Arg.Any<CancellationToken>()
            );
    }
}
