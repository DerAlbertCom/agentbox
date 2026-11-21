using Abox.Naming;
using Abox.Sbx;
using NSubstitute;

namespace Abox.Tests;

public sealed class UserSecretsCommandTests
{
    [Fact]
    public async Task ConfiguredDefaultLocationIsUsedWhenLocationIsOmitted()
    {
        // Arrange
        using var fixture = new UserSecretsCliHarness();
        fixture.Root.File("folder/abox.json", """{"userSecrets":["sample"]}""");
        fixture.Store("sample", "{}");

        // Act
        var exitCode = await fixture.RunDefault();

        // Assert
        exitCode.Should().Be(0);
        await fixture.Sbx.Received(1).ListSandboxesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PreflightFailureDoesNotReachTheDestination()
    {
        // Arrange
        using var fixture = new UserSecretsCliHarness();
        fixture.Root.File("folder/abox.json", """{"userSecrets":["sample"]}""");
        fixture.Store("sample", "{}");
        fixture.Sbx.GetVersionAsync(Arg.Any<CancellationToken>()).Returns(new Version(0, 50, 0));

        // Act
        var exitCode = await fixture.Run();

        // Assert
        exitCode.Should().Be(1);
        await fixture.NoWrites();
        await fixture.Sbx.DidNotReceive().ListSandboxesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CancelledCopyUsesExit130AndExplainsSafeRetryWithoutLeakingCancellationDetails()
    {
        // Arrange
        using var fixture = new UserSecretsCliHarness();
        fixture.Root.File("folder/abox.json", """{"userSecrets":["sample"]}""");
        fixture.Store("sample", "{}");
        fixture
            .Sbx.WriteUserSecretsAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Task.FromException(
                    new OperationCanceledException("synthetic-password 123456789 true")
                )
            );

        // Act
        var exitCode = await fixture.Run("--verbose");

        // Assert
        exitCode.Should().Be(130);
        fixture
            .Console.AllText.Should()
            .Contain("retry")
            .And.Contain("rollback")
            .And.NotContain("synthetic-password")
            .And.NotContain("123456789 true");
    }

    [Fact]
    public async Task DoesNotCreateOrReplaceAMissingSandbox()
    {
        // Arrange
        using var fixture = new UserSecretsCliHarness();
        fixture.Root.File("folder/abox.json", """{"userSecrets":["sample"]}""");
        fixture.Store("sample", "{}");
        fixture
            .Sbx.ListSandboxesAsync(Arg.Any<CancellationToken>())
            .Returns(Array.Empty<SandboxInfo>());

        // Act
        var exitCode = await fixture.Run();

        // Assert
        exitCode.Should().Be(1);
        fixture.Console.AllText.Should().Contain("abox create");
        await fixture.NoWrites();
        await fixture
            .Sbx.DidNotReceive()
            .CreateAsync(Arg.Any<SbxCreateRequest>(), Arg.Any<CancellationToken>());
        await fixture
            .Sbx.DidNotReceive()
            .RemoveSandboxAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("destination")]
    [InlineData("policy")]
    [InlineData("transfer")]
    public async Task TransferAndTargetPreparationFailuresAreValueFreeAndForceDoesNotBypassThem(
        string failurePhase
    )
    {
        // Arrange
        using var fixture = new UserSecretsCliHarness();
        fixture.Configure("""{"squad":"ac","userSecrets":["first","later"]}""");
        fixture.Store("first", "{}");
        fixture.Store("later", "{}");
        if (failurePhase == "destination")
            fixture
                .Sbx.ValidateUserSecretsDestinationAsync(
                    Arg.Any<string>(),
                    Arg.Any<IReadOnlyList<string>>(),
                    Arg.Any<CancellationToken>()
                )
                .Returns(
                    Task.FromException(new AboxException("synthetic-password 123456789 true"))
                );
        else if (failurePhase == "policy")
            fixture
                .Sbx.GetUserSecretsHostPortsAsync(fixture.Name, Arg.Any<CancellationToken>())
                .Returns(
                    Task.FromException<IReadOnlyList<int>>(
                        new AboxException("synthetic-password 123456789 true")
                    )
                );
        else
            fixture
                .Sbx.WriteUserSecretsAsync(
                    Arg.Any<string>(),
                    "later",
                    Arg.Any<string>(),
                    Arg.Any<CancellationToken>()
                )
                .Returns(
                    Task.FromException(new AboxException("synthetic-password 123456789 true"))
                );

        // Act
        var exitCode = await fixture.Run("--force", "--verbose");

        // Assert
        exitCode.Should().Be(1);
        fixture
            .Console.AllText.Should()
            .NotContain("synthetic-password")
            .And.NotContain("123456789 true")
            .And.Contain("retry");
        if (failurePhase != "transfer")
            await fixture.NoWrites();
        else
            fixture.Console.AllText.Should().Contain("1 completed").And.Contain("rollback");
    }
}
